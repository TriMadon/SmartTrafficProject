
import os
import sys
import random


# we need to import python modules from the $SUMO_HOME/tools directory
if 'SUMO_HOME' in os.environ:
    tools = os.path.join(os.environ['SUMO_HOME'], 'tools')
    sys.path.append(tools)
else:
    sys.exit("please declare environment variable 'SUMO_HOME'")

##########################################################################################################################################

def generate_routefile():
    random.seed(42)  # make tests reproducible
    N = 3600  # number of time steps
    # demand per second from different directions
    #pN = 1. / 5
    #pE = 1. / 5
    #pS = 1. / 5
    #pW = 1. / 5
    pN = 0.9
    pE = 0.01
    pS = 0.9
    pW = 0.01
    pLeftTurn = 0.9
    pThrough = 1
    pRightTurn = 1
    
    # rou.xml file (define the route file):
    with open("map.rou.xml", "w") as routes:
        print("""<routes>
    <vType id="carModel" accel="0.8" decel="4.5" sigma="0.5" length="5" minGap="2.5" maxSpeed="16.67" guiShape="passenger"/>

    <route id="N_to_W" edges="N_entry_i N_i W_o W_entry_o" />
    <route id="N_to_S" edges="N_entry_i N_i S_o S_entry_o" />
    <route id="N_to_E" edges="N_entry_i N_i E_o E_entry_o" />
    <route id="E_to_N" edges="E_entry_i E_i N_o N_entry_o" />
    <route id="E_to_W" edges="E_entry_i E_i W_o W_entry_o" />
    <route id="E_to_S" edges="E_entry_i E_i S_o S_entry_o" />
    <route id="S_to_N" edges="S_entry_i S_i N_o N_entry_o" />
    <route id="S_to_W" edges="S_entry_i S_i W_o W_entry_o" />
    <route id="S_to_E" edges="S_entry_i S_i E_o E_entry_o" />
    <route id="W_to_N" edges="W_entry_i W_i N_o N_entry_o" />
    <route id="W_to_E" edges="W_entry_i W_i E_o E_entry_o" />
    <route id="W_to_S" edges="W_entry_i W_i S_o S_entry_o" />
    
    """, file=routes)
        
        # Random traffic generation:
        vehNr = 0
        for i in range(N):
            if random.uniform(0, 1) < pN*pRightTurn:
                print('    <vehicle id="NW_%i" type="carModel" route="N_to_W" depart="%i" color="1,1,1"/>' % (vehNr, i), file=routes)
                vehNr += 1
            if random.uniform(0, 1) < pN*pThrough:
                print('    <vehicle id="NS_%i" type="carModel" route="N_to_S" depart="%i" color="1,1,1"/>' % (vehNr, i), file=routes)
                vehNr += 1
            if random.uniform(0, 1) < pN*pLeftTurn:
                print('    <vehicle id="NE_%i" type="carModel" route="N_to_E" depart="%i" color="1,1,1"/>' % (vehNr, i), file=routes)
                vehNr += 1
            if random.uniform(0, 1) < pE*pRightTurn:
                print('    <vehicle id="EN_%i" type="carModel" route="E_to_N" depart="%i" color="1,1,1"/>' % (vehNr, i), file=routes)
                vehNr += 1
            if random.uniform(0, 1) < pE*pThrough:
                print('    <vehicle id="EW_%i" type="carModel" route="E_to_W" depart="%i" color="1,1,1"/>' % (vehNr, i), file=routes)
                vehNr += 1
            if random.uniform(0, 1) < pE*pLeftTurn:
                print('    <vehicle id="ES_%i" type="carModel" route="E_to_S" depart="%i" color="1,1,1"/>' % (vehNr, i), file=routes)
                vehNr += 1
            if random.uniform(0, 1) < pS*pRightTurn:
                print('    <vehicle id="SE_%i" type="carModel" route="S_to_E" depart="%i" color="1,1,1"/>' % (vehNr, i), file=routes)
                vehNr += 1
            if random.uniform(0, 1) < pS*pThrough:
                print('    <vehicle id="SN_%i" type="carModel" route="S_to_N" depart="%i" color="1,1,1"/>' % (vehNr, i), file=routes)
                vehNr += 1
            if random.uniform(0, 1) < pS*pLeftTurn:
                print('    <vehicle id="SW_%i" type="carModel" route="S_to_W" depart="%i" color="1,1,1"/>' % (vehNr, i), file=routes)
                vehNr += 1
            if random.uniform(0, 1) < pW*pRightTurn:
                print('    <vehicle id="WS_%i" type="carModel" route="W_to_S" depart="%i" color="1,1,1"/>' % (vehNr, i), file=routes)
                vehNr += 1
            if random.uniform(0, 1) < pW*pThrough:
                print('    <vehicle id="WE_%i" type="carModel" route="W_to_E" depart="%i" color="1,1,1"/>' % (vehNr, i), file=routes)
                vehNr += 1
            if random.uniform(0, 1) < pW*pLeftTurn:
                print('    <vehicle id="WN_%i" type="carModel" route="W_to_N" depart="%i" color="1,1,1"/>' % (vehNr, i), file=routes)
                vehNr += 1
        print("</routes>", file=routes)


##########################################################################################################################################


# MAIN #
# this is the main entry point of this script
if __name__ == "__main__":

    # generating route file and random traffic:
    generate_routefile()