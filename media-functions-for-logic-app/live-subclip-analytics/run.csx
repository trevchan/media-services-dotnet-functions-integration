/*
This function submits a job to process a live stream with media analytics.
The first task is a subclipping task that createq a MP4 file, then media analytics are processed on this asset.

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
        "subclip" :
        {
            assetId : "",
            taskId : "",
            start : "",
            duration : ""
        },
        "indexV1" :
        {
            assetId : "",
            taskId : "",
            language : ""
        },
        "indexV2" :
        {
            assetId : "",
            taskId : "",
            language : ""
        },
        "ocr" :
        {
            assetId : "",
            taskId : ""
        },
        "faceDetection" :
        {
            assetId : ""
            taskId : ""
        },
        "faceRedaction" :
        {
            assetId : ""
            taskId : ""
        },
        "motionDetection" :
        {
            assetId : "",
            taskId : ""
        },
        "summarization" :
        {
            assetId : "",
            taskId : ""
        },
        "hyperlapse" :
        {
            assetId : "",
            taskId : ""
        },
        "programId" = programid,
        "channelName" : "",
        "programName" : "",
        "programUrl":"",
        "programState" : "Running",
        "programStateChanged" : "True", // if state changed since last call
        "otherJobsQueue" = 3 // number of jobs in the queue
}
*/

#r "Newtonsoft.Json"
#r "Microsoft.WindowsAzure.Storage"
#r "System.Web"
#r "System.XML"
#r "System.XML.Linq"
#load "../Shared/mediaServicesHelpers.csx"
#load "../Shared/copyBlobHelpers.csx"
#load "../Shared/jobHelpers.csx"

using System;
using System.Net;
using System.Net.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.WindowsAzure.MediaServices.Client;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Web;
using Microsoft.Azure;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.Azure.WebJobs;
using System.Xml;
using System.Xml.Linq;

// Read values from the App.config file.
private static readonly string _mediaServicesAccountName = Environment.GetEnvironmentVariable("AMSAccount");
private static readonly string _mediaServicesAccountKey = Environment.GetEnvironmentVariable("AMSKey");

static string _storageAccountName = Environment.GetEnvironmentVariable("MediaServicesStorageAccountName");
static string _storageAccountKey = Environment.GetEnvironmentVariable("MediaServicesStorageAccountKey");

// Field for service context.
private static CloudMediaContext _context = null;
private static MediaServicesCredentials _cachedCredentials = null;
private static CloudStorageAccount _destinationStorageAccount = null;


public static async Task<object> Run(HttpRequestMessage req, TraceWriter log)
{
    // Variables
    int taskindex = 0;
    int OutputMES = -1;
    int OutputPremium = -1;
    int OutputIndex1 = -1;
    int OutputIndex2 = -1;
    int OutputOCR = -1;
    int OutputFaceDetection = -1;
    int OutputFaceRedaction = -1;
    int OutputMotion = -1;
    int OutputSummarization = -1;
    int OutputHyperlapse = -1;
    int id = 0;
    string programid = "";
    string programName = "";
    string channelName = "";
    string programUrl = "";
    string programState = "";
    string lastProgramState = "";

    IJob job = null;
    ITask taskEncoding = null;
    int NumberJobsQueue = 0;


    int intervalsec = 60; // Interval for each subclip job (sec). Default is 60

    TimeSpan starttime = TimeSpan.FromSeconds(0);
    TimeSpan duration = TimeSpan.FromSeconds(intervalsec);

    log.Info($"Webhook was triggered!");
    string triggerStart = DateTime.UtcNow.ToString("o");

    string jsonContent = await req.Content.ReadAsStringAsync();
    dynamic data = JsonConvert.DeserializeObject(jsonContent);

    log.Info(jsonContent);

    if (data.channelName == null || data.programName == null)
    {
        return req.CreateResponse(HttpStatusCode.BadRequest, new
        {
            error = "Please pass channel name and program name in the input object (channelName, programName)"
        });
    }

    if (data.intervalSec != null)
    {
        intervalsec = (int)data.intervalSec;
    }

    log.Info($"Using Azure Media Services account : {_mediaServicesAccountName}");

    try
    {
        // Create and cache the Media Services credentials in a static class variable.
        _cachedCredentials = new MediaServicesCredentials(
                        _mediaServicesAccountName,
                        _mediaServicesAccountKey);

        // Used the chached credentials to create CloudMediaContext.
        _context = new CloudMediaContext(_cachedCredentials);

        // find the Channel, Program and Asset
        channelName = (string)data.channelName;
        var channel = _context.Channels.Where(c => c.Name == channelName).FirstOrDefault();
        if (channel == null)
        {
            log.Info("Channel not found");
            return req.CreateResponse(HttpStatusCode.BadRequest, new
            {
                error = "Channel not found"
            });
        }

        programName = (string)data.programName;
        var program = channel.Programs.Where(p => p.Name == programName).FirstOrDefault();
        if (program == null)
        {
            log.Info("Program not found");
            return req.CreateResponse(HttpStatusCode.BadRequest, new
            {
                error = "Program not found"
            });
        }

        programState = program.State.ToString();
        programid = program.Id;
        var asset = GetAssetFromProgram(programid);

        if (asset == null)
        {
            log.Info($"Asset not found for program {programid}");
            return req.CreateResponse(HttpStatusCode.BadRequest, new
            {
                error = "Asset not found"
            });
        }

        log.Info($"Using asset Id : {asset.Id}");

        // Table storage to store and real the last timestamp processed
        // Retrieve the storage account from the connection string.
        CloudStorageAccount storageAccount = new CloudStorageAccount(new StorageCredentials(_storageAccountName, _storageAccountKey), true);

        // Create the table client.
        CloudTableClient tableClient = storageAccount.CreateCloudTableClient();

        // Retrieve a reference to the table.
        CloudTable table = tableClient.GetTableReference("liveanalytics");

        // Create the table if it doesn't exist.

        if (!table.CreateIfNotExists())
        {
            log.Info($"Table {table.Name} already exists");
        }
        else
        {
            log.Info($"Table {table.Name} created");
        }

        var lastendtimeInTable = RetrieveLastEndTime(table, programid);

        // Get the manifest data (timestamps)
        var assetmanifestdata = GetManifestTimingData(asset, log);

        log.Info("Timestamps: " + string.Join(",", assetmanifestdata.TimestampList.Select(n => n.ToString()).ToArray()));

        var livetime = TimeSpan.FromSeconds((double)assetmanifestdata.TimestampEndLastChunk / (double)assetmanifestdata.TimeScale);

        log.Info($"Livetime: {livetime}");

        starttime = ReturnTimeSpanOnGOP(assetmanifestdata, livetime.Subtract(TimeSpan.FromSeconds(intervalsec)));
        log.Info($"Value starttime : {starttime}");

        if (lastendtimeInTable != null)
        {
            lastProgramState = lastendtimeInTable.ProgramState;
            log.Info($"Value ProgramState retrieved : {lastProgramState}");

            var lastendtimeInTableValue = TimeSpan.Parse(lastendtimeInTable.LastEndTime);
            log.Info($"Value lastendtimeInTable retrieved : {lastendtimeInTableValue}");

            id = int.Parse(lastendtimeInTable.Id);
            log.Info($"Value id retrieved : {id}");

            if (lastendtimeInTableValue != null)
            {
                var delta = (livetime - lastendtimeInTableValue - TimeSpan.FromSeconds(intervalsec)).Duration();
                log.Info($"Delta: {delta}");

                if (delta < (new TimeSpan(0, 10, 0))) // less than 10 min
                {
                    starttime = lastendtimeInTableValue;
                    log.Info($"Value new starttime : {starttime}");
                }
            }
        }

        duration = livetime - starttime;
        log.Info($"Value duration: {duration}");

        string ConfigurationSubclip = File.ReadAllText(@"D:\home\site\wwwroot\Presets\LiveSubclip.json").Replace("0:00:00.000000", starttime.Subtract(TimeSpan.FromMilliseconds(100)).ToString()).Replace("0:00:30.000000", duration.Add(TimeSpan.FromMilliseconds(200)).ToString());


        int priority = 10;
        if (data.priority != null)
        {
            priority = (int)data.priority;
        }

        // MES Subclipping TASK
        // Declare a new encoding job with the Standard encoder
        job = _context.Jobs.Create("Azure Function - Job for Live Analytics - " + programName, priority);
        // Get a media processor reference, and pass to it the name of the 
        // processor to use for the specific task.
        IMediaProcessor processor = GetLatestMediaProcessorByName("Media Encoder Standard");

        // Change or modify the custom preset JSON used here.
        // string preset = File.ReadAllText("D:\home\site\wwwroot\Presets\H264 Multiple Bitrate 720p.json");

        // Create a task with the encoding details, using a string preset.
        // In this case "H264 Multiple Bitrate 720p" system defined preset is used.
        taskEncoding = job.Tasks.AddNew("Subclipping task",
           processor,
           ConfigurationSubclip,
           TaskOptions.None);

        // Specify the input asset to be encoded.
        taskEncoding.InputAssets.Add(asset);
        OutputMES = taskindex++;

        // Add an output asset to contain the results of the job. 
        // This output is specified as AssetCreationOptions.None, which 
        // means the output asset is not encrypted. 
        var subclipasset = taskEncoding.OutputAssets.AddNew(asset.Name + " subclipped " + triggerStart, AssetCreationOptions.None);

        log.Info($"Adding media analytics tasks");

        // Media Analytics
        OutputIndex1 = AddTask(job, subclipasset, (string)data.indexV1Language, "Azure Media Indexer", "IndexerV1.xml", "English", ref taskindex);
        OutputIndex2 = AddTask(job, subclipasset, (string)data.indexV2Language, "Azure Media Indexer 2 Preview", "IndexerV2.json", "EnUs", ref taskindex);
        OutputOCR = AddTask(job, subclipasset, (string)data.ocrLanguage, "Azure Media OCR", "OCR.json", "AutoDetect", ref taskindex);
        OutputFaceDetection = AddTask(job, subclipasset, (string)data.faceDetectionMode, "Azure Media Face Detector", "FaceDetection.json", "PerFaceEmotion", ref taskindex);
        OutputFaceRedaction = AddTask(job, subclipasset, (string)data.faceRedactionMode, "Azure Media Redactor", "FaceRedaction.json", "combined", ref taskindex, priority - 1);
        OutputMotion = AddTask(job, subclipasset, (string)data.motionDetectionLevel, "Azure Media Motion Detector", "MotionDetection.json", "medium", ref taskindex, priority - 1);
        OutputSummarization = AddTask(job, subclipasset, (string)data.summarizationDuration, "Azure Media Video Thumbnails", "Summarization.json", "0.0", ref taskindex);
        OutputHyperlapse = AddTask(job, subclipasset, (string)data.hyperlapseSpeed, "Azure Media Hyperlapse", "Hyperlapse.json", "8", ref taskindex);

        job.Submit();
        log.Info("Job Submitted");

        id++;
        UpdateLastEndTime(table, starttime + duration, programid, id, program.State);

        log.Info($"Output MES index {OutputMES}");

        // Let store some data in altid of subclipped asset
        var sid = ReturnId(job, OutputMES);
        log.Info($"SID {sid}");
        var subclipassetrefreshed = _context.Assets.Where(a => a.Id == sid).FirstOrDefault();
        log.Info($"subclipassetrefreshed ID {subclipassetrefreshed.Id}");
        subclipassetrefreshed.AlternateId = JsonConvert.SerializeObject(new SubclipInfo() { programId = programid, subclipStart = starttime, subclipDuration = duration });
        subclipassetrefreshed.Update();

        // Let store some data in altid of index assets
        var index1sid = ReturnId(job, OutputIndex1);
        if (index1sid != null)
        {
            var index1assetrefreshed = _context.Assets.Where(a => a.Id == index1sid).FirstOrDefault();
            log.Info($"index1assetrefreshed ID {index1assetrefreshed.Id}");
            index1assetrefreshed.AlternateId = JsonConvert.SerializeObject(new SubclipInfo() { programId = programid, subclipStart = starttime, subclipDuration = duration });
            index1assetrefreshed.Update();
        }

        var index2sid = ReturnId(job, OutputIndex2);
        if (index2sid != null)
        {
            var index2assetrefreshed = _context.Assets.Where(a => a.Id == index2sid).FirstOrDefault();
            log.Info($"index2assetrefreshed ID {index2assetrefreshed.Id}");
            index2assetrefreshed.AlternateId = JsonConvert.SerializeObject(new SubclipInfo() { programId = programid, subclipStart = starttime, subclipDuration = duration });
            index2assetrefreshed.Update();
        }

        // Get program URL
        var publishurlsmooth = GetValidOnDemandURI(asset);

        if (publishurlsmooth != null)
        {
            programUrl = publishurlsmooth.ToString();
        }

        NumberJobsQueue = _context.Jobs.Where(j => j.State == JobState.Queued).Count();

    }
    catch (Exception ex)
    {
        log.Info($"Exception {ex}");
        return req.CreateResponse(HttpStatusCode.InternalServerError, new
        {
            Error = ex.ToString()
        });
    }

    log.Info("Job Id: " + job.Id);
    log.Info("Output asset Id: " + ((OutputMES > -1) ? ReturnId(job, OutputMES) : ReturnId(job, OutputPremium)));

    return req.CreateResponse(HttpStatusCode.OK, new
    {
        triggerStart = triggerStart,
        jobId = job.Id,
        subclip = new
        {
            assetId = ReturnId(job, OutputMES),
            taskId = ReturnTaskId(job, OutputMES),
            start = starttime,
            duration = duration,
        },
        indexV1 = new
        {
            assetId = ReturnId(job, OutputIndex1),
            taskId = ReturnTaskId(job, OutputIndex1),
            language = (string)data.indexV1Language
        },
        indexV2 = new
        {
            assetId = ReturnId(job, OutputIndex2),
            taskId = ReturnTaskId(job, OutputIndex2),
            language = (string)data.indexV2Language,
        },
        ocr = new
        {
            assetId = ReturnId(job, OutputOCR),
            taskId = ReturnTaskId(job, OutputOCR)
        },
        faceDetection = new
        {
            assetId = ReturnId(job, OutputFaceDetection),
            taskId = ReturnTaskId(job, OutputFaceDetection)
        },
        faceRedaction = new
        {
            assetId = ReturnId(job, OutputFaceRedaction),
            taskId = ReturnTaskId(job, OutputFaceRedaction)
        },
        motionDetection = new
        {
            assetId = ReturnId(job, OutputMotion),
            taskId = ReturnTaskId(job, OutputMotion)
        },
        summarization = new
        {
            assetId = ReturnId(job, OutputSummarization),
            taskId = ReturnTaskId(job, OutputSummarization)
        },
        hyperlapse = new
        {
            assetId = ReturnId(job, OutputHyperlapse),
            taskId = ReturnTaskId(job, OutputHyperlapse)
        },
        channelName = channelName,
        programName = programName,
        programId = programid,
        programUrl = programUrl,
        programState = programState,
        programStateChanged = (lastProgramState != programState).ToString(),
        otherJobsQueue = NumberJobsQueue
    });
}


public static EndTimeInTable RetrieveLastEndTime(CloudTable table, string programID)
{
    TableOperation tableOperation = TableOperation.Retrieve<EndTimeInTable>(programID, "lastEndTime");
    TableResult tableResult = table.Execute(tableOperation);
    return tableResult.Result as EndTimeInTable;
}

public static void UpdateLastEndTime(CloudTable table, TimeSpan endtime, string programId, int id, ProgramState state)
{
    var EndTimeInTableEntity = new EndTimeInTable();
    EndTimeInTableEntity.ProgramId = programId;
    EndTimeInTableEntity.Id = id.ToString();
    EndTimeInTableEntity.ProgramState = state.ToString();
    EndTimeInTableEntity.LastEndTime = endtime.ToString();
    EndTimeInTableEntity.AssignPartitionKey();
    EndTimeInTableEntity.AssignRowKey();
    TableOperation tableOperation = TableOperation.InsertOrReplace(EndTimeInTableEntity);
    table.Execute(tableOperation);
}

static IAsset GetAssetFromProgram(string programId)
{
    IAsset asset = null;

    try
    {
        IProgram program = _context.Programs.Where(p => p.Id == programId).FirstOrDefault();
        if (program != null)
        {
            asset = program.Asset;
        }
    }
    catch
    {
    }
    return asset;
}


// return the exact timespan on GOP
static public TimeSpan ReturnTimeSpanOnGOP(ManifestTimingData data, TimeSpan ts)
{
    var response = ts;
    ulong timestamp = (ulong)(ts.TotalSeconds * data.TimeScale);

    int i = 0;
    foreach (var t in data.TimestampList)
    {
        if (t < timestamp && i < (data.TimestampList.Count - 1) && timestamp < data.TimestampList[i + 1])
        {
            response = TimeSpan.FromSeconds((double)t / (double)data.TimeScale);
            break;
        }
        i++;
    }
    return response;
}


static public ManifestTimingData GetManifestTimingData(IAsset asset, TraceWriter log)
// Parse the manifest and get data from it
{
    ManifestTimingData response = new ManifestTimingData() { IsLive = false, Error = false, TimestampOffset = 0, TimestampList = new List<ulong>() };

    try
    {
        ILocator mytemplocator = null;
        Uri myuri = GetValidOnDemandURI(asset);
        if (myuri == null)
        {
            mytemplocator = CreatedTemporaryOnDemandLocator(asset);
            myuri = GetValidOnDemandURI(asset);
        }
        if (myuri != null)
        {
            log.Info($"Asset URI {myuri.ToString()}");

            XDocument manifest = XDocument.Load(myuri.ToString());

            //log.Info($"manifest {manifest}");
            var smoothmedia = manifest.Element("SmoothStreamingMedia");
            var videotrack = smoothmedia.Elements("StreamIndex").Where(a => a.Attribute("Type").Value == "video");

            // TIMESCALE
            string timescalefrommanifest = smoothmedia.Attribute("TimeScale").Value;
            if (videotrack.FirstOrDefault().Attribute("TimeScale") != null) // there is timescale value in the video track. Let's take this one.
            {
                timescalefrommanifest = videotrack.FirstOrDefault().Attribute("TimeScale").Value;
            }
            ulong timescale = ulong.Parse(timescalefrommanifest);
            response.TimeScale = (timescale == TimeSpan.TicksPerSecond) ? null : (ulong?)timescale; // if 10000000 then null (default)

            // Timestamp offset
            if (videotrack.FirstOrDefault().Element("c").Attribute("t") != null)
            {
                response.TimestampOffset = ulong.Parse(videotrack.FirstOrDefault().Element("c").Attribute("t").Value);
            }
            else
            {
                response.TimestampOffset = 0; // no timestamp, so it should be 0
            }

            ulong totalduration = 0;
            ulong durationpreviouschunk = 0;
            ulong durationchunk;
            int repeatchunk;
            foreach (var chunk in videotrack.Elements("c"))
            {
                durationchunk = chunk.Attribute("d") != null ? ulong.Parse(chunk.Attribute("d").Value) : 0;
                log.Info($"duration d {durationchunk}");

                repeatchunk = chunk.Attribute("r") != null ? int.Parse(chunk.Attribute("r").Value) : 1;
                log.Info($"repeat r {repeatchunk}");
                totalduration += durationchunk * (ulong)repeatchunk;

                if (chunk.Attribute("t") != null)
                {
                    //totalduration = ulong.Parse(chunk.Attribute("t").Value) - response.TimestampOffset; // new timestamp, perhaps gap in live stream....
                    response.TimestampList.Add(ulong.Parse(chunk.Attribute("t").Value));
                    log.Info($"t value {ulong.Parse(chunk.Attribute("t").Value)}");
                }
                else
                {
                    response.TimestampList.Add(response.TimestampList[response.TimestampList.Count() - 1] + durationpreviouschunk);
                }

                for (int i = 1; i < repeatchunk; i++)
                {
                    response.TimestampList.Add(response.TimestampList[response.TimestampList.Count() - 1] + durationchunk);
                }

                durationpreviouschunk = durationchunk;

            }
            response.TimestampEndLastChunk = response.TimestampList[response.TimestampList.Count() - 1] + durationpreviouschunk;

            if (smoothmedia.Attribute("IsLive") != null && smoothmedia.Attribute("IsLive").Value == "TRUE")
            { // Live asset.... No duration to read (but we can read scaling and compute duration if no gap)
                response.IsLive = true;
                response.AssetDuration = TimeSpan.FromSeconds((double)totalduration / ((double)timescale));
            }
            else
            {
                totalduration = ulong.Parse(smoothmedia.Attribute("Duration").Value);
                response.AssetDuration = TimeSpan.FromSeconds((double)totalduration / ((double)timescale));
            }
        }
        else
        {
            response.Error = true;
        }
        if (mytemplocator != null) mytemplocator.Delete();
    }
    catch (Exception ex)
    {
        response.Error = true;
    }
    return response;
}


public static ILocator CreatedTemporaryOnDemandLocator(IAsset asset)
{
    ILocator tempLocator = null;

    try
    {
        var locatorTask = Task.Factory.StartNew(() =>
        {
            try
            {
                tempLocator = asset.GetMediaContext().Locators.Create(LocatorType.OnDemandOrigin, asset, AccessPermissions.Read, TimeSpan.FromHours(1));
            }
            catch
            {
                throw;
            }
        });
        locatorTask.Wait();
    }
    catch (Exception ex)
    {
        throw ex;
    }

    return tempLocator;
}


public class ManifestTimingData
{
    public TimeSpan AssetDuration { get; set; }
    public ulong TimestampOffset { get; set; }
    public ulong? TimeScale { get; set; }
    public bool IsLive { get; set; }
    public bool Error { get; set; }
    public List<ulong> TimestampList { get; set; }
    public ulong TimestampEndLastChunk { get; set; }
}

public class SubclipInfo
{
    public TimeSpan subclipStart { get; set; }
    public TimeSpan subclipDuration { get; set; }
    public string programId { get; set; }
}


public class EndTimeInTable : TableEntity
{
    private string programId;
    private string lastendtime;
    private string id;
    private string programState;

    public void AssignRowKey()
    {
        this.RowKey = "lastEndTime";
    }
    public void AssignPartitionKey()
    {
        this.PartitionKey = programId;
    }
    public string ProgramId
    {
        get
        {
            return programId;
        }

        set
        {
            programId = value;
        }
    }
    public string LastEndTime
    {
        get
        {
            return lastendtime;
        }

        set
        {
            lastendtime = value;
        }
    }
    public string Id
    {
        get
        {
            return id;
        }

        set
        {
            id = value;
        }
    }
    public string ProgramState
    {
        get
        {
            return programState;
        }

        set
        {
            programState = value;
        }
    }
}