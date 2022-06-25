import os
import time
import pandas as pd
import torch
import keyboard
import json
import cv2 as cv

pd.set_option("display.max_rows", None, "display.max_columns", None)
model = torch.hub.load('ultralytics/yolov5', 'yolov5s6')  # or yolov5m, yolov5l, yolov5x, custom
model.classes = [2]  # Picking only the car class numbered 2
model.conf = 0.15

dictionary = {
    "north": 0,
    "east": 0,
    "south": 0,
    "west": 0
}
backPath = os.path.normpath(os.getcwd() + os.sep + os.pardir).replace(os.sep, '/')
roadMask = cv.imread(backPath + '/traffic3d/Traffic3D/Assets/buffer/NewestMask.jpg', 0)

while True:

    try:
        # Images
        northImg = cv.imread(backPath + '/traffic3d/Traffic3D/Assets/buffer/N.jpg')  # or file, Path, PIL, OpenCV, numpy, list
        eastImg = cv.imread(backPath + '/traffic3d/Traffic3D/Assets/buffer/E.jpg')
        southImg = cv.imread(backPath + '/traffic3d/Traffic3D/Assets/buffer/S.jpg')
        westImg = cv.imread(backPath + '/traffic3d/Traffic3D/Assets/buffer/W.jpg')

        northMasked = cv.cvtColor(cv.bitwise_and(northImg, northImg, mask=roadMask)[30:, :], cv.COLOR_BGR2RGB)
        eastMasked = cv.cvtColor(cv.bitwise_and(eastImg, eastImg, mask=roadMask)[30:, :], cv.COLOR_BGR2RGB)
        southMasked = cv.cvtColor(cv.bitwise_and(southImg, southImg, mask=roadMask)[30:, :], cv.COLOR_BGR2RGB)
        westMasked = cv.cvtColor(cv.bitwise_and(westImg, westImg, mask=roadMask)[30:, :], cv.COLOR_BGR2RGB)

        resultsN = model(northMasked)
        resultsE = model(eastMasked)
        resultsS = model(southMasked)
        resultsW = model(westMasked)

        if keyboard.is_pressed("up"):
            resultsN.show()
        if keyboard.is_pressed("down"):
            resultsS.show()
        if keyboard.is_pressed("left"):
            resultsW.show()
        if keyboard.is_pressed("right"):
            resultsE.show()

        # Results
        northCount = resultsN.pandas().xyxy[0].shape[0]  # or .show(), .save(), .crop(), .pandas(), etc.
        eastCount = resultsE.pandas().xyxy[0].shape[0]
        southCount = resultsS.pandas().xyxy[0].shape[0]
        westCount = resultsW.pandas().xyxy[0].shape[0]

        dictionary['north'] = northCount
        dictionary['east'] = eastCount
        dictionary['south'] = southCount
        dictionary['west'] = westCount

        json_object = json.dumps(dictionary, indent=4)
        with open(backPath + "/traffic3d/Traffic3D/Assets/buffer/CONx.json", "w") as outfile:
            outfile.write(json_object)

        time.sleep(0.3)

    except:
        pass


