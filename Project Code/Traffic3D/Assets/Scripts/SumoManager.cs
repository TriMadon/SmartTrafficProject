using CodingConnected.TraCI.NET;
using CodingConnected.TraCI.NET.Types;
using CodingConnected.TraCI.NET.Commands;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CsvHelper;
using CsvHelper.Configuration;
using Newtonsoft.Json;
using UnityEngine;

public class SumoManager : MonoBehaviour
{
    #region Declarations and Instantiations

    /*********************************************** DECLARATIONS/INSTANTIATIONS ***********************************************/
    private static SumoManager _instance;
    private const int SIMULATION_RUNS_PER_SECOND = 60;
    private const int SUMO_TIME_OUT = 5;
    private const int YELLOW_TIME = 3;
    private const int ALL_RED_TIME = 1;
    private const int dataTimeCap = 600;
    private int _step;
    private int[] _carCounts = new int[4];
    private static int _currentTime;
    [SerializeField] private ControlAlgorithm controlAlgorithm;
    [SerializeField] private bool countsAreFromYolo;
    [SerializeField] private bool activateFuzzyLogic;
    [SerializeField] private int greenTime;
    [SerializeField] private bool printVehicleCounts;
    [SerializeField] private bool recordWaitingTimeReadings;
    [SerializeField] private bool recordFuelReadings;
    // ReSharper disable once InconsistentNaming
    [SerializeField] private bool recordCO2Readings;
    private Coroutine _takeScreenShotsCR;
    private Coroutine _fixedTimeCR;
    private Coroutine _maxCountCR;
    private List<WaitingTimeData> _timeDataObjects;
    private List<FuelConsumptionData> _fuelConsumptionObjects;
    private List<Co2Data> _co2EmissionObjects;

    public static SumoManager GetInstance()
    {
        return _instance;
    }

    private void Awake()
    {
        _instance = this;
        _currentTime = 0;
    }

    public string ip = "127.0.0.1";
    public int port = 4001;
    public List<SumoLinkControlPointObject> sumoControlSettings = new List<SumoLinkControlPointObject>();
    private bool _connected;
    private TraCIClient _client;
    private LaneAreaDetectorCommands _laneDetector;
    private SimulationCommands _simCommands;
    // ReSharper disable once NotAccessedField.Local
    private ControlCommands _conCommands;
    private EdgeCommands _edgeCommands;
    private VehicleCommands _vehicleCommands;
    private VehicleFactory _vehicleFactory;
    private FuzzyEngine _fuzzyEngine;
    private readonly Dictionary<string, GameObject> _renderedVehicles = new Dictionary<string, GameObject>();
    // ReSharper disable once UnusedMember.Local
    private Dictionary<Junction, int> _junctionShotCount = new Dictionary<Junction, int>();
    private readonly List<SumoTrafficLight> _sumoTrafficLights = new List<SumoTrafficLight>();
    #endregion


    #region Start

    /*********************************************** START ***********************************************/
    void Start()
    {
        _vehicleFactory = FindObjectOfType<VehicleFactory>();
        string filePath = Path.Combine(Application.dataPath, "Sumo");
        ImportAndGenerate.parseXMLfiles(filePath);
        ImportAndGenerate.CreateStreetNetwork();
        _client = new TraCIClient();
        _laneDetector = new LaneAreaDetectorCommands(_client);
        _simCommands = new SimulationCommands(_client);
        _conCommands = new ControlCommands(_client);
        _edgeCommands = new EdgeCommands(_client);
        _vehicleCommands = new VehicleCommands(_client);
        _fuzzyEngine = new FuzzyEngine();
        _takeScreenShotsCR = null;
        _fixedTimeCR = null;
        _maxCountCR = null;
        greenTime = 30;
        _timeDataObjects = new List<WaitingTimeData>();
        _fuelConsumptionObjects = new List<FuelConsumptionData>();
        _co2EmissionObjects = new List<Co2Data>();


        _fuzzyEngine.Initialize();

        if (_client.Connect(ip, port))
        {
            Debug.Log("Connected to Sumo");
            _connected = true;
        }
        else
        {
            Debug.Log("Unable to connect to Sumo");
            this.enabled = false;
            return;
        }

        StartCoroutine(Run());
        _vehicleFactory.StopAllCoroutines();
        TrafficLightManager.GetInstance().RefreshTrafficLightsAndJunctions();
        if (!IsControlledBySumo(SumoLinkControlPoint.TRAFFIC_FLOW))
        {
            Debug.Log("Demand Controlled By Traffic3D");
            StartCoroutine(RunTraffic3DTrafficFlow());
        }
        List<string> junctionIds = _client.TrafficLight.GetIdList().Content;
        foreach (string id in junctionIds)
        {
            List<string> controlledLanes = _client.TrafficLight.GetControlledLanes(id).Content;
            string currentState = _client.TrafficLight.GetState(id).Content;
            for (int i = 0; i < controlledLanes.Count; i++)
            {
                TrafficLight trafficLight = TrafficLightManager.GetInstance().GetTrafficLight(controlledLanes[i]);
                if (trafficLight != null)
                {
                    SumoTrafficLight sumoTrafficLight = _sumoTrafficLights.Find(s => s.trafficLight.trafficLightId.Equals(trafficLight.trafficLightId));
                    if (sumoTrafficLight == null)
                    {
                        _sumoTrafficLights.Add(new SumoTrafficLight(trafficLight, id, new HashSet<int>() { i }));
                    }
                    else
                    {
                        sumoTrafficLight.AddIndexState(i);
                    }
                }
            }
            if (!IsControlledBySumo(SumoLinkControlPoint.TRAFFIC_LIGHTS))
            {
                _client.TrafficLight.SetRedYellowGreenState(id, new string('r', currentState.Length));
                _client.TrafficLight.SetPhaseDuration(id, Double.MaxValue);
            }
        }

        if (IsControlledBySumo(SumoLinkControlPoint.TRAFFIC_LIGHTS))
        {
            Debug.Log("Traffic Lights Controlled By SUMO");
            TrafficLightManager.GetInstance().StopAllCoroutines();
            StartCoroutine(RunTrafficLights());
        }
        else
        {
            TrafficLightManager.GetInstance().trafficLightChangeEvent += ChangeSumoTrafficLights;

            foreach (tlLogicType tlLogicType in ImportAndGenerate.trafficLightPrograms.Values)
            {
                int stateCounter = 0;
                Junction junction = FindObjectsOfType<Junction>().ToList().Find(j => j.junctionId.Equals(tlLogicType.id));
                List<SumoTrafficLight> sumoTrafficLightsForJunction = _sumoTrafficLights.FindAll(sumoTrafficLight => sumoTrafficLight.junctionId.Equals(tlLogicType.id));
                foreach (object obj in tlLogicType.Items)
                {
                    if (obj is phaseType)
                    {
                        stateCounter++;
                        GameObject stateObject = new GameObject("State" + stateCounter);
                        stateObject.transform.SetParent(junction.gameObject.transform);
                        JunctionState junctionState = stateObject.AddComponent<JunctionState>();
                        junctionState.stateNumber = stateCounter;
                        junctionState.trafficLightStates = new JunctionState.TrafficLightState[sumoTrafficLightsForJunction.Count()];
                        int trafficLightStateCounter = 0;
                        phaseType phase = (phaseType)obj;

                        foreach (SumoTrafficLight sumoTrafficLight in sumoTrafficLightsForJunction)
                        {
                            TrafficLight.LightColour lightColour = sumoTrafficLight.GetLightColourFromStateString(phase.state);
                            junctionState.trafficLightStates[trafficLightStateCounter] = new JunctionState.TrafficLightState(sumoTrafficLight.trafficLight.trafficLightId, lightColour);
                            trafficLightStateCounter++;
                        }
                    }
                }
            }
        }

        StartCoroutine(WriteWaitingTime());
        StartCoroutine(WriteFuelData());
        StartCoroutine(WriteCo2Data());

    }
    #endregion


    #region IEnumerators

    /*********************************************** IENUMERATORS ***********************************************/

    #region Run
    IEnumerator Run()
    {
        while (true)
        {
            yield return new WaitForSeconds(1f / SIMULATION_RUNS_PER_SECOND);
            // ReSharper disable once RedundantArgumentDefaultValue
            var task = Task.Run(() => _client.Control.SimStep(0.0));
            if (!task.Wait(TimeSpan.FromSeconds(SUMO_TIME_OUT)))
            {
                throw new Exception("Sumo Timed out");
            }
        }
    }
    #endregion

    #region RunTraffic3DTrafficFlow
    IEnumerator RunTraffic3DTrafficFlow()
    {
        while (true)
        {
            yield return new WaitForSeconds(RandomNumberGenerator.GetInstance().Range(_vehicleFactory.lowRangeRespawnTime, _vehicleFactory.highRangeRespawnTime));
            if (_renderedVehicles.Count < RandomNumberGenerator.GetInstance().Range(_vehicleFactory.slowDownVehicleRateAt, _vehicleFactory.maximumVehicleCount))
            {
                AddVehicle();
            }
        }
        // ReSharper disable once IteratorNeverReturns
    }
    #endregion

    #region WriteWaitingTime
    // Write the waiting time data using TraCI commands into CSV:
    IEnumerator WriteWaitingTime()
    {
        var savedTime = 0;
        var hasReachedTimeCap = false;
        var configWaitingTimeData = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
        };
        var path = Directory.GetCurrentDirectory().Replace("\\", "/") + "/Assets/Results/new data/WaitingTimeData.csv";
        while (true)
        {
            yield return new WaitForSeconds(0.1f);

            var difference = _currentTime - savedTime;
            if (difference < 5)
                continue;

            savedTime = _currentTime;
            var waitingTimeN = _edgeCommands.GetWaitingTime("N_i").Content;
            var waitingTimeE = _edgeCommands.GetWaitingTime("E_i").Content;
            var waitingTimeS = _edgeCommands.GetWaitingTime("S_i").Content;
            var waitingTimeW = _edgeCommands.GetWaitingTime("W_i").Content;

            _timeDataObjects.Add(new WaitingTimeData(){
                TimeStepProperty = savedTime,
                WaitingTimePropertyN = waitingTimeN,
                WaitingTimePropertyE = waitingTimeE,
                WaitingTimePropertyS = waitingTimeS,
                WaitingTimePropertyW = waitingTimeW
            });

            if (_currentTime > dataTimeCap)
                hasReachedTimeCap = true;

            if (recordWaitingTimeReadings != true && !hasReachedTimeCap) continue;
            var writer = MakeFile(path);
            using (var csv = new CsvWriter(writer, configWaitingTimeData))
            {
                csv.WriteRecords(_timeDataObjects);
            }
            recordWaitingTimeReadings = false;
            hasReachedTimeCap = false;
            print("Waiting time file has been written!");
        }
        // ReSharper disable once IteratorNeverReturns
    }
    #endregion

    #region WriteFuelData
    // Write the fuel consumption data using TraCI commands into CSV:
    IEnumerator WriteFuelData()
    {
        var savedTime = 0;
        var hasReachedTimeCap = false;
        var configFuelData = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
        };
        var path = Directory.GetCurrentDirectory().Replace("\\", "/") + "/Assets/Results/new data/FuelData.csv";
        while (true)
        {
            yield return new WaitForSeconds(0.1f);

            var difference = _currentTime - savedTime;
            if (difference < 5)
                continue;

            savedTime = _currentTime;
            var fuelN = GetFuelConsumptionOfStoppedVehicles("N");
            var fuelE = GetFuelConsumptionOfStoppedVehicles("E");
            var fuelS = GetFuelConsumptionOfStoppedVehicles("S");
            var fuelW = GetFuelConsumptionOfStoppedVehicles("W");

            _fuelConsumptionObjects.Add(new FuelConsumptionData(){
                TimeStepProperty = savedTime,
                FuelPropertyN = fuelN,
                FuelPropertyE = fuelE,
                FuelPropertyS = fuelS,
                FuelPropertyW = fuelW,
            });

            if (_currentTime > dataTimeCap)
                hasReachedTimeCap = true;

            if (recordFuelReadings != true && !hasReachedTimeCap) continue;
            var writer = MakeFile(path);
            using (var csv = new CsvWriter(writer, configFuelData))
            {
                csv.WriteRecords(_fuelConsumptionObjects);
            }
            recordFuelReadings = false;
            hasReachedTimeCap = false;
            print("Fuel file has been written!");
        }
        // ReSharper disable once IteratorNeverReturns
    }
    #endregion

    #region WriteCo2Data
    // Write the CO2 Emissions data using TraCI commands into CSV:
    IEnumerator WriteCo2Data()
    {
        var savedTime = 0;
        var hasReachedTimeCap = false;
        var configCo2Data = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
        };
        var path = Directory.GetCurrentDirectory().Replace("\\", "/") + "/Assets/Results/new data/Co2Data.csv";
        while (true)
        {
            yield return new WaitForSeconds(0.1f);

            var difference = _currentTime - savedTime;
            if (difference < 5)
                continue;

            savedTime = _currentTime;
            var co2N = GetCO2EmissionsOfStoppedVehicles("N");
            var co2E = GetCO2EmissionsOfStoppedVehicles("E");
            var co2S = GetCO2EmissionsOfStoppedVehicles("S");
            var co2W = GetCO2EmissionsOfStoppedVehicles("W");

            _co2EmissionObjects.Add(new Co2Data(){
                TimeStepProperty = savedTime,
                Co2PropertyN = co2N,
                Co2PropertyE = co2E,
                Co2PropertyS = co2S,
                Co2PropertyW = co2W,
            });

            if (_currentTime > dataTimeCap)
                hasReachedTimeCap = true;

            if (recordCO2Readings != true && !hasReachedTimeCap) continue;
            var writer = MakeFile(path);
            using (var csv = new CsvWriter(writer, configCo2Data))
            {
                csv.WriteRecords(_co2EmissionObjects);
            }
            recordCO2Readings = false;
            hasReachedTimeCap = false;
            print("Fuel file has been written!");
        }
        // ReSharper disable once IteratorNeverReturns
    }
    #endregion

    #region MakeFile

    private StreamWriter MakeFile(string path)
    {
        if (File.Exists(path))
            File.WriteAllText(path, string.Empty);
        var stream = File.Open(path, File.Exists(path) ? FileMode.Append : FileMode.CreateNew);
        var writer = new StreamWriter(stream);
        return writer;
    }

        #endregion

    #region RunTrafficLights
    // Let SUMO control the traffic lights:
    IEnumerator RunTrafficLights()
    {
        var currentAlgorithm = ControlAlgorithm.MAX_COUNT;

        while (true)
        {
            yield return new WaitForEndOfFrame();

            if (currentAlgorithm == controlAlgorithm)
                continue;

            if (_takeScreenShotsCR != null)
                StopCoroutine(_takeScreenShotsCR);
            if (_maxCountCR != null)
                StopCoroutine(_maxCountCR);
            if (_fixedTimeCR != null)
                StopCoroutine(_fixedTimeCR);

            switch (controlAlgorithm)
            {
                case ControlAlgorithm.MAX_COUNT:
                    _maxCountCR = StartCoroutine(MaxCountAlgorithm());
                    currentAlgorithm = ControlAlgorithm.MAX_COUNT;
                    break;
                case ControlAlgorithm.FIXED_SEQUENCE:
                    _fixedTimeCR = StartCoroutine(FixedTimeAlgorithm());
                    currentAlgorithm = ControlAlgorithm.FIXED_SEQUENCE;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
    #endregion

    #region MaxCountAlgorithm
    // Find maximum vehicle counts (Lane Detector or YOLO):
    IEnumerator MaxCountAlgorithm()
    {
        var savedTime = 0;
        double localGreenTime = greenTime;
        var resetTime = false;
        var isTakingScreenShots = false;
        SetNorthGreen("tl_0");

        while (true)
        {
            yield return new WaitForSeconds(0.1F);

            if (countsAreFromYolo && isTakingScreenShots == false)
            {
                _takeScreenShotsCR = StartCoroutine(TakeScreenShots());
                isTakingScreenShots = true;
            }

            if (!countsAreFromYolo && isTakingScreenShots)
            {
                StopCoroutine(_takeScreenShotsCR);
            }
            // Sensor selection
            _carCounts = IsFromYolo() ? GetCountsFromYolo() : GetCountsFromE2();

            if (printVehicleCounts)
            {
                print("North: " + _carCounts[0] + " | " + "East: " + _carCounts[1] + " | " +
                      "South: " + _carCounts[2] + " | " + "West: " + _carCounts[3]);
            }

            var difference = _currentTime - savedTime;
            if (difference < localGreenTime)
                continue;

            savedTime = _currentTime;

            // Iterate over traffic lights via their ID's:
            foreach (string id in _client.TrafficLight.GetIdList().Content)
            {
                var mostCrowdedPhase = _carCounts.ToList().IndexOf(_carCounts.Max());

                if (GetCurrentPhaseAsInt(id) != mostCrowdedPhase)
                {
                    yield return StartCoroutine(SetYellow(id, YELLOW_TIME));
                    yield return StartCoroutine(SetAllRed(id, ALL_RED_TIME));
                }

                switch (mostCrowdedPhase)
                {
                    case 0: { SetNorthGreen(id); break; }
                    case 1: { SetEastGreen(id); break; }
                    case 2: { SetSouthGreen(id); break; }
                    case 3: { SetWestGreen(id); break; }
                }
            }

            if (activateFuzzyLogic)
            {
                var maxCount = _carCounts.Max();
                localGreenTime = _fuzzyEngine.DoInference(maxCount);
                resetTime = true;
            }
            else if (resetTime && activateFuzzyLogic == false)
            {
                localGreenTime = 30;
                resetTime = false;
            }
        }
        // ReSharper disable once IteratorNeverReturns
    }
    #endregion

    #region FixedTimeAlgorithm
    // Fixed time algorithm:
    private IEnumerator FixedTimeAlgorithm()
    {
        var savedTime = 0;
        var turn = 0;
        double localGreenTime = greenTime;
        var isTakingScreenShots = false;
        var resetTime = false;


        SetWestGreen("tl_0");
        while (true)
        {
            yield return new WaitForSeconds(0.1f);

            if (countsAreFromYolo && isTakingScreenShots == false)
            {
                _takeScreenShotsCR = StartCoroutine(TakeScreenShots());
                isTakingScreenShots = true;
            }

            if (!countsAreFromYolo && isTakingScreenShots)
            {
                StopCoroutine(_takeScreenShotsCR);
            }

            _carCounts = IsFromYolo() ? GetCountsFromYolo() : GetCountsFromE2();

            foreach (var id in _client.TrafficLight.GetIdList().Content)
            {
                var difference = _currentTime - savedTime;
                if (difference < localGreenTime)
                    continue;

                savedTime = _currentTime;

                yield return StartCoroutine(SetYellow(id, YELLOW_TIME));
                yield return StartCoroutine(SetAllRed(id, ALL_RED_TIME));

                switch (turn % 4)
                {
                    case 0: { SetNorthGreen(id); break; }
                    case 1: { SetEastGreen(id); break; }
                    case 2: { SetSouthGreen(id); break; }
                    case 3: { SetWestGreen(id); break; }
                }

                if (activateFuzzyLogic)
                {
                    var currentPhaseCount = _carCounts[turn % 4];
                    localGreenTime = _fuzzyEngine.DoInference(currentPhaseCount);
                    resetTime = true;
                }
                else if (resetTime && activateFuzzyLogic == false)
                {
                    localGreenTime = 30;
                    resetTime = false;
                }

                turn++;
            }
        }
        // ReSharper disable once IteratorNeverReturns
    }
    #endregion

    #region TakeScreenShots
    // Camera screenshots
    // ReSharper disable Unity.PerformanceAnalysis
    IEnumerator TakeScreenShots()
    {
        yield return new WaitForSeconds(0.1f);
        var junction = TrafficLightManager.GetInstance().GetJunction("tl_0");      // Select the SUMO junction

        // Define the added cameras:
        var nc = junction.GetCameraList()[0];
        var ec = junction.GetCameraList()[1];
        var sc = junction.GetCameraList()[2];
        var wc = junction.GetCameraList()[3];

        nc.targetDisplay = 0;
        ec.targetDisplay = 1;
        sc.targetDisplay = 2;
        wc.targetDisplay = 3;

        // Attach the CameraManager script to the added cameras:
        var cameraManagerN = nc.GetComponent<CameraManager>();
        cameraManagerN.SetPhase("N");
        var cameraManagerE = ec.GetComponent<CameraManager>();
        cameraManagerE.SetPhase("E");
        var cameraManagerS = sc.GetComponent<CameraManager>();
        cameraManagerS.SetPhase("S");
        var cameraManagerW = wc.GetComponent<CameraManager>();
        cameraManagerW.SetPhase("W");

        while (true)
        {
            yield return new WaitForSeconds(0.75f);
            yield return StartCoroutine(cameraManagerN.CaptureScreenshot());
            yield return new WaitForSeconds(0.25f);
            yield return StartCoroutine(cameraManagerE.CaptureScreenshot());
            yield return new WaitForSeconds(0.25f);
            yield return StartCoroutine(cameraManagerS.CaptureScreenshot());
            yield return new WaitForSeconds(0.25f);
            yield return StartCoroutine(cameraManagerW.CaptureScreenshot());
            GC.Collect();
        }
        // ReSharper disable once IteratorNeverReturns
    }
    #endregion

    #region SetYellow
    // Set the traffic light to Yellow for a set amount of time:
    private IEnumerator SetYellow(string id, int delayTime)
    {
        var currentState = _client.TrafficLight.GetState(id).GetContentAs<string>();
        var newState = currentState.Replace("G", "y");
        _client.TrafficLight.SetRedYellowGreenState(id, newState);
        yield return StartCoroutine(DelayTL(delayTime));
    }
    #endregion

    #region SetAllRed
    // Set the traffic light to Yellow for a set amount of time:
    private IEnumerator SetAllRed(string id, int delayTime)
    {
        // ReSharper disable once StringLiteralTypo
        _client.TrafficLight.SetRedYellowGreenState(id, "rrrrrrrrrrrrrrrrrrrr");
        yield return StartCoroutine(DelayTL(delayTime));
    }
    #endregion

    #region DelayTL
    // Responsible for the amount of delay added to a traffic light color state
    IEnumerator DelayTL(int delayTime)
    {
        int initialTime = _currentTime;
        while (true)
        {
            yield return new WaitForSeconds(0.05f);
            if (_currentTime - initialTime >= delayTime)
                break;
        }
    }
    #endregion

    #endregion


    #region Methods

    /*********************************************** METHODS ***********************************************/

    #region GetCountsFromYolo
    // Read counts from YOLO via CONx.json
    private int[] GetCountsFromYolo()
    {
        try
        {
            var allDetections =
                File.ReadAllText(Directory.GetCurrentDirectory().Replace("\\", "/") + "/Assets/buffer/CONx.json");
            var detectorCountMap = JsonConvert.DeserializeObject<Dictionary<string, int>>(allDetections);

            var fromJson = new int[4];
            if (detectorCountMap == null) return fromJson;

            fromJson[0] = detectorCountMap["north"];
            fromJson[1] = detectorCountMap["east"];
            fromJson[2] = detectorCountMap["south"];
            fromJson[3] = detectorCountMap["west"];

            return fromJson;
        }

        catch (Exception)
        {
            return GetCountsFromE2();
        }
    }
    #endregion

    #region GetFuelConsumptionOfStoppedVehicles

    private double GetFuelConsumptionOfStoppedVehicles(string edge)
    {
        List<string> GetVehicleIDs(string lane)
        {
            return _laneDetector.GetLastStepVehicleIds(lane).Content;
        }

        var lane1Vehicles = GetVehicleIDs(edge + "_0_det");
        var lane2Vehicles = GetVehicleIDs(edge + "_1_det");
        var lane3Vehicles = GetVehicleIDs(edge + "_2_det");
        var vehicleIDs = lane1Vehicles.Concat(lane2Vehicles).Concat(lane3Vehicles).ToList();
        var fuelConsumption = 0.0;

        // ReSharper disable once ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator
        foreach (var vehicle in vehicleIDs)
        {
            if (_vehicleCommands.GetSpeed(vehicle).Content == 0.0 || Math.Abs(_vehicleCommands.GetAcceleration(vehicle).Content) > 0.0)
                fuelConsumption += _vehicleCommands.GetFuelConsumption(vehicle).Content;
        }

        return fuelConsumption;
    }

    #endregion

    #region GetCO2EmissionsOfStoppedVehicles

    // ReSharper disable once InconsistentNaming
    private double GetCO2EmissionsOfStoppedVehicles(string edge)
    {
        List<string> GetVehicleIDs(string lane)
        {
            return _laneDetector.GetLastStepVehicleIds(lane).Content;
        }

        var lane1Vehicles = GetVehicleIDs(edge + "_0_det");
        var lane2Vehicles = GetVehicleIDs(edge + "_1_det");
        var lane3Vehicles = GetVehicleIDs(edge + "_2_det");
        var vehicleIDs = lane1Vehicles.Concat(lane2Vehicles).Concat(lane3Vehicles).ToList();
        // ReSharper disable once InconsistentNaming
        var CO2Emission = 0.0;

        foreach (var vehicle in vehicleIDs)
        {
            if (_vehicleCommands.GetSpeed(vehicle).Content == 0.0 || Math.Abs(_vehicleCommands.GetAcceleration(vehicle).Content) > 0.0)
                CO2Emission += _vehicleCommands.GetCO2Emission(vehicle).Content;
        }

        return CO2Emission;
    }

    #endregion

    #region GetCurrentPhaseAsInt
    // Return the current traffic light state
    private int GetCurrentPhaseAsInt(string id)
    {
        var currentState = _client.TrafficLight.GetState(id).GetContentAs<string>();
        // ReSharper disable once ConvertSwitchStatementToSwitchExpression
        switch (currentState)
        {
            case "GGGGGrrrrrrrrrrrrrrr": return 0;      // North
            case "rrrrrGGGGGrrrrrrrrrr": return 1;      //East
            case "rrrrrrrrrrGGGGGrrrrr": return 2;      //South
            case "rrrrrrrrrrrrrrrGGGGG": return 3;      //West
            default: return 0;
        }
    }
    #endregion

    #region GetCountsFromE2
    // Read counts from Lane Detectors via TraCI:
    private int[] GetCountsFromE2()
    {
        int GetCount(string st)
        {
            return _laneDetector.GetLastStepVehicleNumber(st).Content;
        }

        var nCount = GetCount("N_0_det") + GetCount("N_1_det") + GetCount("N_2_det");
        var eCount = GetCount("E_0_det") + GetCount("E_1_det") + GetCount("E_2_det");
        var sCount = GetCount("S_0_det") + GetCount("S_1_det") + GetCount("S_2_det");
        var wCount = GetCount("W_0_det") + GetCount("W_1_det") + GetCount("W_2_det");

        int[] laneDetectorCounts = {nCount, eCount, sCount, wCount};
        return laneDetectorCounts;
    }
    #endregion

    #region ChangeSumoTrafficLights
    // Change the traffic light in Unity based on SUMO
    // ReSharper disable Unity.PerformanceAnalysis
    private void ChangeSumoTrafficLights(object sender, TrafficLight.TrafficLightChangeEventArgs trafficLightChangeEvent)
    {
        SumoTrafficLight sumoTrafficLight = _sumoTrafficLights.Find(s => s.trafficLight.trafficLightId.Equals(trafficLightChangeEvent.trafficLight.trafficLightId));
        if (sumoTrafficLight == null)
        {
            Debug.Log("Unable to find sumo traffic light: " + trafficLightChangeEvent.trafficLight.trafficLightId);
            return;
        }
        var currentState = _client.TrafficLight.GetState(sumoTrafficLight.junctionId).Content;
        var newState = sumoTrafficLight.GetStateFromTrafficLightColour(currentState);
        _client.TrafficLight.SetRedYellowGreenState(sumoTrafficLight.junctionId, newState);
    }
    #endregion

    #region IsConnected
    // Flag to check if SUMO is connected to Unity:
    public bool IsConnected()
    {
        return _connected;
    }
    #endregion

    #region AddVehicle
    // Traffic3D way to spawn vehicles based on SUMO routes picked randomly:
    public void AddVehicle()
    {
        _client.Vehicle.Add(Guid.NewGuid().ToString(), "DEFAULT_VEHTYPE", GetRandomSumoRoute(), 0, 0, 0, 0);
    }
    #endregion

    #region GetRandomSumoRoute
    // Go to the .rou.xml file and pick random routes from it:
    private string GetRandomSumoRoute()
    {
        return ImportAndGenerate.routes.Keys.ToArray()[RandomNumberGenerator.GetInstance().Range(0, ImportAndGenerate.routes.Count)];
    }
    #endregion

    #region IsControlledBySumo
    // Check what setting (Flow or Traffic Light) is controlled by SUMO:
    public bool IsControlledBySumo(SumoLinkControlPoint sumoLinkControlPoint)
    {
        SumoLinkControlPointObject controlPoint = sumoControlSettings.Find(controlSetting => controlSetting.sumoLinkControlPoint == sumoLinkControlPoint);      // Find the setting selected
        if (controlPoint == null || !controlPoint.controlledBySumo)
        {
            return false;
        }
        return true;
    }
    #endregion

    #region IsFromYolo
    // Flag to check if the counts are comming from YOLO
    private bool IsFromYolo()
    {
        return countsAreFromYolo;
    }
    #endregion

    #region Traffic Light State Setters
    // Set North phase to Green
    private void SetNorthGreen(string id)
    {
        _client.TrafficLight.SetRedYellowGreenState(id, "GGGGGrrrrrrrrrrrrrrr");
    }

    // Set East phase to Green
    private void SetEastGreen(string id)
    {
        _client.TrafficLight.SetRedYellowGreenState(id, "rrrrrGGGGGrrrrrrrrrr");
    }

    // Set South phase to Green
    private void SetSouthGreen(string id)
    {
        _client.TrafficLight.SetRedYellowGreenState(id, "rrrrrrrrrrGGGGGrrrrr");
    }

    // Set West phase to Green
    private void SetWestGreen(string id)
    {
        _client.TrafficLight.SetRedYellowGreenState(id, "rrrrrrrrrrrrrrrGGGGG");
    }
    #endregion

    #region SavePeakWaitingTime
    // Saves the peak waiting time for all phases
    // ReSharper disable once UnusedMember.Local
    private void SavePeakWaitingTime(int phaseNumber)
    {
        switch (phaseNumber)
        {
            case 0:
            {
                var waitingTimeN = _edgeCommands.GetWaitingTime("N_i").Content;
                _timeDataObjects.Add(new WaitingTimeData(){TimeStepProperty = _currentTime, WaitingTimePropertyN = waitingTimeN});
                break;
            }
            case 1:
            {
                var waitingTimeE = _edgeCommands.GetWaitingTime("E_i").Content;
                _timeDataObjects.Add(new WaitingTimeData(){TimeStepProperty = _currentTime, WaitingTimePropertyE = waitingTimeE});
                break;
            }
            case 2:
            {
                var waitingTimeS = _edgeCommands.GetWaitingTime("S_i").Content;
                _timeDataObjects.Add(new WaitingTimeData(){TimeStepProperty = _currentTime, WaitingTimePropertyS = waitingTimeS});
                break;
            }
            case 3:
            {
                var waitingTimeW = _edgeCommands.GetWaitingTime("W_i").Content;
                _timeDataObjects.Add(new WaitingTimeData(){TimeStepProperty = _currentTime, WaitingTimePropertyW = waitingTimeW});
                break;
            }
        }
    }
    #endregion

    #endregion


    #region Update
    /*********************************************** UPDATE ***********************************************/

    // Contains a global time variable. Create, Update and Destroy rendered vehicles:
    void Update()
    {
        _currentTime = _simCommands.GetCurrentTime("tl_0").Content/1000;      // Global SUMO simulation time
        TraCIResponse<List<string>> vehicleIDs = _client.Vehicle.GetIdList();
        foreach (string vehicleId in vehicleIDs.Content)
        {
            if (!_renderedVehicles.ContainsKey(vehicleId))
            {
                CreateRenderedVehicle(vehicleId);
            }
            Position3D position = _client.Vehicle.GetPosition3D(vehicleId).Content;
            double yawAngle = _client.Vehicle.GetAngle(vehicleId).Content;
            UpdateRenderedVehicle(vehicleId, position, (float)yawAngle);
        }
        List<string> vehiclesToDestroy = new List<string>();
        foreach (string id in _renderedVehicles.Keys)
        {
            if (!vehicleIDs.Content.Contains(id))
            {
                vehiclesToDestroy.Add(id);
            }
        }
        foreach (string id in vehiclesToDestroy)
        {
            DestroyRenderedVehicle(id);
        }
        GC.Collect();
    }


    // Remove a vehicle:
    private void DestroyRenderedVehicle(string id)
    {
        Destroy(_renderedVehicles[id].gameObject);
        _renderedVehicles.Remove(id);
    }

    // Spawn a vehicle:
    private void CreateRenderedVehicle(string id)
    {
        var vehicle = Instantiate(_vehicleFactory.GetRandomVehicle()).gameObject;
        vehicle.GetComponent<Rigidbody>().isKinematic = true;
        vehicle.GetComponent<Vehicle>().enabled = false;
        vehicle.GetComponent<VehicleDriver>().enabled = false;
        vehicle.GetComponent<VehicleEngine>().enabled = false;
        foreach (BoxCollider boxCollider in vehicle.GetComponentsInChildren<BoxCollider>())
        {
            boxCollider.enabled = false;
        }
        _renderedVehicles.Add(id, vehicle);
    }

    // Update the spawned vehicle:
    private void UpdateRenderedVehicle(string id, Position3D position3D, float angle)
    {
        _renderedVehicles[id].transform.position = new Vector3((float)position3D.X, (float)position3D.Z, (float)position3D.Y);
        _renderedVehicles[id].transform.rotation = Quaternion.Euler(0, angle, 0);
    }
    #endregion


    #region Serializable Fields
    // Add interactive fields to Unity:
    [Serializable]
    public class SumoLinkControlPointObject
    {
        public SumoLinkControlPoint sumoLinkControlPoint;
        public bool controlledBySumo;
    }
    #endregion

}

