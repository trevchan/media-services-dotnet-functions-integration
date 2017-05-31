# Logic Apps which use Azure Functions and Azure Media Services

## First Logic App : Simple VOD workflow

### Presentation

This template creates a Logic app that listens to an onedrive folder and will copy it to an Azure Media Services asset, triggers an encoding job, publish the output asset and send an email when the process is complete.

![Screen capture](images/logicapp1-simplevod-1.png?raw=true)
![Screen capture](images/logicapp1-simplevod-2.png?raw=true)

[See the detailed view of the logic app.](logicapp1-simplevod-screen.md)

### 1. Prerequisite
If not already done : fork the repo, deploy Azure Functions and select the **"media-functions-for-logic-app"** Project (IMPORTANT!)

Follow the guidelines in the [git tutorial](1-CONTRIBUTION-GUIDE/git-tutorial.md) for details on how to fork the project and use Git properly with this project.

Note : if you never provided your GitHub account in the Azure portal before, the continous integration probably will probably fail and you won't see the functions. In that case, you need to setup it manually. Go to your azure functions deployment / Functions app settings / Configure continous integration. Select GitHub as a source and configure it to use your fork.

<a href="https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2FAzure-Samples%2Fmedia-services-dotnet-functions-integration%2Fmaster%2Fazuredeploy.json" target="_blank">
    <img src="http://azuredeploy.net/deploybutton.png"/>
</a>

### 2. Deploy the logic app

<a href="https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2FAzure-Samples%2Fmedia-services-dotnet-functions-integration%2Fmaster%2Fmedia-functions-for-logic-app%2Flogicapp1-simplevod-deploy.json" target="_blank">
    <img src="http://azuredeploy.net/deploybutton.png"/>
</a>

![Screen capture](images/form-logicapp1-simplevod.png?raw=true)

It is recommended to use the same resource group for the functions and the logic app.
The functions and Logic App must be deployed in the same region.
Please specify the name of the storage account used by Media Services.

### 3. Fix the connections

When deployed, go to the Logic App Designer and fix the connections (Onedrive, Outlook.com...). Make sure to (re)select the OneDrive folder that you want to use for the ingest.

### 4. Start the AMS streaming endpoint

To enable streaming, go to the Azure portal, select the Azure Media Services account which as been created, and start the default streaming endpoint.

![Screen capture](images/start-se-1.png?raw=true)

![Screen capture](images/start-se-2.png?raw=true)

## Second Logic App : using Azure Storage trigger

This is the same workflow that the first logic app with two main differences:
- the source is monitored using blob trigger (new file coming to an Azure Storage container)
- the asset creation / blob copy is done through Azure functions to workaround the limitation of 50 MB. These functions have been tested with 1.8 GB files.

<a href="https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2FAzure-Samples%2Fmedia-services-dotnet-functions-integration%2Fmaster%2Fmedia-functions-for-logic-app%2Flogicapp2-simplevod-storage-deploy.json" target="_blank">
    <img src="http://azuredeploy.net/deploybutton.png"/>
</a>

![Screen capture](images/logicapp2-1.png?raw=true)

## Third Logic App : An advanced VOD workflow

This template creates a Logic app which

* monitors a container in Azure Storage (blob trigger),
* copies new file to an Azure Media Services asset,
* triggers an encoding job,
* converts the English audio to text (using Media Indexer v2),
* translates the English subtitles to French (using Bing translator),
* copies back the French subtitles to the subtitles asset,
* publishes the output assets,
* generates a short playback URL (using bitlink)
* sends an email with Outlook when the process is complete or if the job failed. In the email, the playback link includes the two subtitles.

<a href="https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2FAzure-Samples%2Fmedia-services-dotnet-functions-integration%2Fmaster%2Fmedia-functions-for-logic-app%2Flogicapp3-advancedvod-deploy.json" target="_blank">
    <img src="http://azuredeploy.net/deploybutton.png"/>
</a>

![Screen capture](images/logicapp3-advancedvod-1.png?raw=true)
![Screen capture](images/logicapp3-advancedvod-2.png?raw=true)
![Screen capture](images/logicapp3-advancedvod-3.png?raw=true)

## Functions documentation
This section list the functions available and describes the input and output parameters.

### create-empty-asset

This function creates an empty asset.
```c#
Input:
{
    "assetName" : "the name of the asset"
}

Output:
{
    "assetId" : "the Id of the asset created",
    "containerPath" : "the url to the storage container of the asset"
}
```

### start-blob-copy-to-asset

This function copy a file (blob) to a new asset previously created.
```c#
Input:
{
    "assetId" : "the Id of the asset where the file must be copied",
    "fileName" : "filename.mp4",
    "sourceStorageAccountName" : "",
    "sourceStorageAccountKey": "",
    "sourceContainer" : ""
}
Output:
{
 "destinationContainer": "" // container of asset
}
```

### check-blob-copy-to-asset-status

This function monitor the copy of files (blobs) to a new asset previously created.
```c#
Input:
{
      "destinationContainer" : "mycontainer",
      "delay": 15000 // optional (default is 5000)
}
Output:
{
      "copyStatus": 2 // status
      "isRunning" : "True"
      "isSuccessful" : "False"
}
```

### sync-asset

This function create the asset files based on the blobs in the asset container.
```c#
Input:
{
    "assetId" : "the Id of the asset"
}
```

### submit-job

This function submits a job wth encoding and/or analytics.
```c#
Input:
{
    "assetId" : "nb:cid:UUID:2d0d78a2-685a-4b14-9cf0-9afb0bb5dbfc", // Mandatory, Id of the source asset
    "mesPreset" : "Adaptive Streaming",         // Optional but required to encode with Media Encoder Standard (MES). If mesPreset contains an extension "H264 Multiple Bitrate 720p with thumbnail.json" then it loads this file from ..\Presets
    "workflowAssetId" : "nb:cid:UUID:2d0d78a2-685a-4b14-9cf0-9afb0bb5dbfc", // Optional, but required to encode the asset with Premium Workflow Encoder. Id for the workflow asset
    "indexV1Language" : "English",              // Optional but required to index the asset with Indexer v1
    "indexV2Language" : "EnUs",                 // Optional but required to index the asset with Indexer v2
    "ocrLanguage" : "AutoDetect" or "English",  // Optional but required to do OCR
    "faceDetectionMode" : "PerFaceEmotion,      // Optional but required to trigger face detection
    "faceRedactionMode" : "analyze",            // Optional, but required for face redaction
    "motionDetectionLevel" : "medium",          // Optional, required for motion detection
    "summarizationDuration" : "0.0",            // Optional. Required to create video summarization. "0.0" for automatic
    "hyperlapseSpeed" : "8",                    // Optional, required to hyperlapse the video
    "priority" : 10,                            // Optional, priority of the job
    "useEncoderOutputForAnalytics" : true       // Optional, use generated asset by MES or Premium Workflow as a source for media analytics (except hyperlapse)
    "jobName" : ""                              // Optional, job name
}

Output:
{
    "jobId" :  // job id
    "mes" : // Output asset generated by MES (if mesPreset was specified)
        {
            assetId : "",
            taskId : ""
        },
    "mepw" : // Output asset generated by Premium Workflow Encoder
        {
            assetId : "",
            taskId : ""
        },
    "indexV1" :  // Output asset generated by Indexer v1
        {
            assetId : "",
            taskId : "",
            language : ""
        },
    "indexV2" : // Output asset generated by Indexer v2
        {
            assetId : "",
            taskId : "",
            language : ""
        },
    "ocr" : // Output asset generated by OCR
        {
            assetId : "",
            taskId : ""
        },
    "faceDetection" : // Output asset generated by Face detection
        {
            assetId : ""
            taskId : ""
        },
    "faceRedaction" : // Output asset generated by Face redaction
        {
            assetId : ""
            taskId : ""
        },
     "motionDetection" : // Output asset generated by motion detection
        {
            assetId : "",
            taskId : ""
        },
     "summarization" : // Output asset generated by video summarization
        {
            assetId : "",
            taskId : ""
        },
     "hyperlapse" : // Output asset generated by Hyperlapse
        {
            assetId : "",
            taskId : ""
        }
 }
```

### check-job-status

This function chevck a job status.
```c#
Input:
{
    "jobId" : "nb:jid:UUID:1ceaa82f-2607-4df9-b034-cd730dad7097", // Mandatory, Id of the source asset
    "extendedInfo" : true // optional. Returns ams account unit size, nb units, nb of jobs in queue, scheduled and running states. Only if job is complete or error
 }

Output:
{
    "jobState" : 2,				// The state of the job (int)
    "isRunning" : "False",      // True if job is running
    "isSuccessful" : "True",    // True is job is a success. Only valid if IsRunning = False
    "errorText" : ""			// error(s) text if job state is error
    "startTime" :""
    "endTime" : "",
    "runningDuration" : ""
    "extendedInfo" :			// if extendedInfo is true and job is finished or in error
    {
        "mediaUnitNumber" = 2,
        "mediaUnitSize" = "S2",
        "otherJobsProcessing" = 2,
        "otherJobsScheduled" = 1,
        "otherJobsQueue" = 1,
        "amsAccountName" = "accountname"
    }
 }
```

### check-task-status

This function chevck a task status.
```c#
Input:
{
    "jobId" : "nb:jid:UUID:1ceaa82f-2607-4df9-b034-cd730dad7097", // Mandatory, Id of the job
    "taskId" : "nb:tid:UUID:cdc25b10-3ed7-4005-bcf9-6222b35b5be3", // Mandatory, Id of the task
    "extendedInfo" : true // optional. Returns ams account unit size, nb units, nb of jobs in queue, scheduled and running states. Only if job is complete or error
 }

Output:
{
    "taskState" : 2,			// The state of the task (int)
    "isRunning" : "False",      // True if job is running
    "isSuccessful" : "True",    // True is job is a success. Only valid if IsRunning = False
    "errorText" : ""			// error(s) text if task state is error
    "startTime" :""
    "endTime" : "",
    "runningDuration" : ""
    "extendedInfo" :			// if extendedInfo is true and job is finished or in error
    {
        "mediaUnitNumber" = 2,
        "mediaUnitSize" = "S2",
        "otherJobsProcessing" = 2,
        "otherJobsScheduled" = 1,
        "otherJobsQueue" = 1,
        "amsAccountName" = "accountname"
    }
 }
```

### publish-asset

This function publishes an asset.
```c#
Input:
{
    "assetId" : "nb:cid:UUID:2d0d78a2-685a-4b14-9cf0-9afb0bb5dbfc", // Mandatory, Id of the source asset
}

Output:
{
    playerUrl : "", // Url of demo AMP with content
    smoothUrl : "", // Url for the published asset (contains name.ism/manifest at the end) for dynamic packaging
    pathUrl : ""    // Url of the asset (path)
}
```

### return-subtitles

This function returns subtitles from an asset.
```c#
Input:
{
    "assetId" : "nb:cid:UUID:88432c30-cb4a-4496-88c2-b2a05ce9033b", // Mandatory, Id of the source asset
    "timeOffset" :"00:01:00", // optional, offset to add to subtitles (used for live analytics)
    "deleteAsset" : true // Optional, delete the asset once data has been read from it
 }

Output:
{
    "vttUrl" : "",      // the full path to vtt file if asset is published
    "ttmlUrl" : "",     // the full path to vtt file if asset is published
    "pathUrl" : "",     // the path to the asset if asset is published
    "vttDocument" : "", // the full vtt document,
    "vttDocumentOffset" : "", // the full vtt document with offset
    "ttmlDocument : ""  // the full ttml document
    "ttmlDocumentOffset : ""  // the full ttml document with offset
 }
```

### return-analytics

his function returns media analytics from an asset.
```c#
Input:
{
    "assetFaceRedactionId" : "nb:cid:UUID:88432c30-cb4a-4496-88c2-b2a05ce9033b", // Id of the source asset that contains media analytics (face redaction)
    "assetMotionDetectionId" : "nb:cid:UUID:88432c30-cb4a-4496-88c2-b2a05ce9033b",  // Id of the source asset that contains media analytics (motion detection)
    "assetOcrId" : "nb:cid:UUID:88432c30-cb4a-4496-88c2-b2a05ce9033b",  // Id of the source asset that contains media analytics (OCR)
    "timeOffset" :"00:01:00", // optional, offset to add to subtitles (used for live analytics)
    "copyToContainer" : "jpgfaces" // Optional, to copy jpg files to a specific container in the same storage account. Use lowercases as this is the container name and there are restrictions. Used as a prefix, as date is added at the end (yyyyMMdd)
    "copyToContainerAccountName" : "jhggjgghggkj" // storage account name. optional. if not provided, ams storage account is used
    "copyToContainerAccountKey" "" // storage account key
    "deleteAsset" : true // Optional, delete the asset(s) once data has been read from it
 }

Output:
{
    "faceRedaction" :
        {
        "json" : "",      // the json of the face redaction
        "jsonOffset" : "",      // the json of the face redaction with offset
        "jpgFaces":[
                {
                    "id" :24,
                    "fileId": "nb:cid:UUID:a93464ae-cbd5-4e63-9459-a3e2cf869f0e",
                    "fileName": "ArchiveTopBitrate_video_800000_thumb000024.jpg",
                    "url" : "http://xpouyatdemo.streaming.mediaservices.windows.net/903f9261-d745-48aa-8efd-ebcd6e6128d6/ArchiveTopBitrate_video_800000_thumb000024.jpg"
                }
                ]
        "pathUrl" : "",     // the path to the asset if asset is published
        },
    "motionDetection":
        {
        "json" : "",      // the json of the face redaction
        "jsonOffset" : ""      // the json of the face redaction with offset
        }
    "Ocr":
        {
        "json" : "",      // the json of the Ocr
        "jsonOffset" : ""      // the json of Ocr with offset
        }
 }
```

### add-textfile-to-asset

This function adds a text file to an existing asset.
As a option, the text can be converted from ttml to vtt (useful when the ttml has been translated with MS Translator and the user wants a VTT file for Azure Media Player).

```c#
Input:
{
    "document" : "", // content of the text file to create
    "fileName" : "subtitle-en.ttml", // file name to create
    "assetId" : "nb:cid:UUID:88432c30-cb4a-4496-88c2-b2a05ce9033b", // Mandatory, Id of the asset
    "convertTtml" :true // optional, convert the document from ttml to vtt, and create another file in the asset : subtitle-en.vtt
}

Output:
{
}
```


### delete-entity

This function delete AMS entities like job(s) and/or asset(s).
Several asset ids or job ids can be passed (with a coma separator).

```c#
Input:
{
    "jobID": "nb:jid:UUID:7f566f5e-be9c-434f-bb7b-101b2e24f27e,nb:jid:UUID:58f9e85a-a889-4205-baa1-ecf729f9c753",     // job(s) id. Coma delimited if several job ids 
    "assetId" : "nb:cid:UUID:61926f1d-69ba-4386-a90e-e27803104853,nb:cid:UUID:b4668bc4-2899-4247-b339-429025153ab9"   // asset(s) id.
}

Output:
{
}
```


### set-media-ru

This function sets the number and speed of media reserved units in the account.

```c#
Input:
{
    "ruCount" : "+1", // can be a number like "1", or a number with + or - to increase or decrease the number. Example :  "+2" or "-3"
    "ruSpeed" : "S1"  // can be "S1", "S2" or "S3"
}

Output:
{
    "success" : "True", // return if operation is a success or not
    "maxRu" : 10,       // number of max units
    "newRuCount" : 3,   // new count of units
    "newRuSpeed" : "S2" // new speed of units
}
```


### live-subclip-analytics

This function submits a job to process a live stream with media analytics.
The first task is a subclipping task that createq a MP4 file, then media analytics are processed on this asset.

```c#
Input:
{
    "channelName": "channel1",      // Mandatory
    "programName" : "program1",     // Mandatory
    "intervalSec" : 60              // Optional. Default is 60 seconds. The duration of subclip (and interval between two calls)
    "indexV1Language" : "English",  // Optional
    "indexV2Language" : "EnUs",     // Optional
    "ocrLanguage" : "AutoDetect" or "English",  // Optional
    "faceDetectionMode" : "PerFaceEmotion,      // Optional
    "faceRedactionMode" : "analyze",            // Optional, but required for face redaction
    "motionDetectionLevel" : "medium",          // Optional
    "summarizationDuration" : "0.0",            // Optional. 0.0 for automatic
    "hyperlapseSpeed" : "8"                     // Optional
    "priority" : 10                             // Optional. Priority of the job
}

Output:
{
        "triggerStart" : "" // date and time when the function was called
        "jobId" :  // job id
         subclip :
        {
            assetId : "",
            taskId : "",
            start : "",
            duration : ""
        },
        indexV1 :
        {
            assetId : "",
            taskId : "",
            language : ""
        },
        indexV2 :
        {
            assetId : "",
            taskId : "",
            language : ""
        },
        ocr :
        {
            assetId : "",
            taskId : ""
        },
        faceDetection :
        {
            assetId : ""
            taskId : ""
        },
          faceRedaction :
        {
            assetId : ""
            taskId : ""
        },
        motionDetection :
        {
            assetId : "",
            taskId : ""
        },
        summarization :
        {
            assetId : "",
            taskId : ""
        },
        hyperlapse :
        {
            assetId : "",
            taskId : ""
        },
        "programId" = programid,
        "channelName" : "",
        "programName" : "",
        "programUrl" : "",
        "programState" : "Running",
        "programStateChanged" : "True", // if state changed since last call
        "otherJobsQueue" = 3 // number of jobs in the queue
}
```