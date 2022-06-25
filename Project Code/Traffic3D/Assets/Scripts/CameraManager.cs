using UnityEngine;
using System.IO;
using System;
using System.Collections;

public class CameraManager : MonoBehaviour {

    //Set your screenshot resolutions
    public int captureWidth = 768;
    public int captureHeight = 500;
    // configure with raw, jpg, png, or ppm (simple raw format)
    public enum Format { Raw, JPG, PNG, Ppm };
    public Format format = Format.JPG;
    // folder to write output (defaults to data path)
    public string outputFolder;
    private Rect _rect;
    private RenderTexture _renderTexture;
    private Texture2D _screenShot;
    private string _phase;
    private bool _isRenderTextureNull;
    private Camera _oneCamera;

    public void SetPhase(string phaseString)
    {
        this._phase = phaseString;
    }
    private void Start()
    {
        _oneCamera = GetComponent<Camera>();
        _isRenderTextureNull = _renderTexture == null;
        outputFolder = Directory.GetCurrentDirectory().Replace("\\", "/") + "/Assets/buffer";
        if(!Directory.Exists(outputFolder))
        {
            Directory.CreateDirectory(outputFolder);
            Debug.Log("Save Path will be : " + outputFolder);
        }
        Debug.Log(outputFolder);
    }

    private string CreateFileName(string lane)
    {
        var filename = $"{outputFolder}/{lane}.{format.ToString().ToLower()}";
        return filename;
    }

    public IEnumerator CaptureScreenshot()
    {
        yield return new WaitForEndOfFrame();
        try
        {
            // create screenshot objects
            if (_isRenderTextureNull)
            {
                _rect = new Rect(0, 0, captureWidth, captureHeight);
                _renderTexture = new RenderTexture(captureWidth, captureHeight, 24);
                _screenShot = new Texture2D(captureWidth, captureHeight, TextureFormat.RGB24, false);
            }

            _oneCamera.targetTexture = _renderTexture;
            _oneCamera.Render();
            RenderTexture.active = _renderTexture;
            _screenShot.ReadPixels(_rect, 0, 0);
            _oneCamera.targetTexture = null;
            RenderTexture.active = null;
            // get our filename
            var filename = CreateFileName(_phase);
            // get file header/data bytes for the specified image format
            byte[] fileHeader = null;
            byte[] fileData;
            switch (format)
            {
                //Set the format and encode based on it
                case Format.Raw:
                    fileData = _screenShot.GetRawTextureData();
                    break;
                case Format.PNG:
                    fileData = _screenShot.EncodeToPNG();
                    break;
                case Format.JPG:
                    fileData = _screenShot.EncodeToJPG();
                    break;
                //For ppm files
                case Format.Ppm:
                default:
                {
                    // create a file header - ppm files
                    var headerStr = $"P6\n{_rect.width} {_rect.height}\n255\n";
                    fileHeader = System.Text.Encoding.ASCII.GetBytes(headerStr);
                    fileData = _screenShot.GetRawTextureData();
                    break;
                }
            }

            // create new thread to offload the saving from the main thread
            new System.Threading.Thread(() =>
            {
                var file = File.Create(filename);
                if (fileHeader != null)
                {
                    file.Write(fileHeader, 0, fileHeader.Length);
                }

                file.Write(fileData, 0, fileData.Length);
                file.Close();
            }).Start();
            //Cleanup
            Destroy(_renderTexture);
            _renderTexture = null;
            _screenShot = null;
            GC.Collect();
        }

        catch (Exception)
        {
            // ignored
        }
    }
}
