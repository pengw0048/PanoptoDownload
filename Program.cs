using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;

namespace PanoptoDownload
{
    [DataContract] class SessionResults
    {
        [DataMember] public SessionResultsD d;
    }
    [DataContract] class SessionResultsD
    {
        [DataMember] public SessionData[] Results;
        [DataMember] public int TotalNumber;
    }
    [DataContract] class SessionData
    {
        [DataMember] public double AverageRating;
        [DataMember] public string DeliveryID;
        [DataMember] public double Duration;
        [DataMember] public string FolderName;
        [DataMember] public string SessionID;
        [DataMember] public string SessionName;
        [DataMember] public string StartTime;
        [DataMember] public string ThumbUrl;
    }
    [DataContract] class DeliveryInfo
    {
        [DataMember] public DeliveryDetail Delivery;
        [DataMember] public string InvocationId;
        [DataMember] public string PodcastInvocationUrl;
    }
    [DataContract] class DeliveryDetail
    {
        [DataMember] public string PublicID;
        [DataMember] public string SessionGroupLongName;
        [DataMember] public string SessionGroupPublicID;
        [DataMember] public string SessionName;
        [DataMember] public string SessionPublicID;
        [DataMember] public DStream[] Streams;
        [DataMember] public DTimestamp[] Timestamps;

    }
    [DataContract] class DStream
    {
        [DataMember] public double AbsoluteEnd;
        [DataMember] public double AbsoluteStart;
        [DataMember] public string PublicID;
        [DataMember] public double RelativeEnd;
        [DataMember] public DRelativeSegment[] RelativeSegments;
        [DataMember] public double RelativeStart;
        [DataMember] public string StreamHttpUrl;
        [DataMember] public string StreamUrl;
        [DataMember] public string Tag;
    }
    [DataContract] class DRelativeSegment
    {
        [DataMember] public double RelativeStart;
        [DataMember] public double Start;
        [DataMember] public double End;
        [DataMember] public string StreamPublicID;
    }
    [DataContract] class DTimestamp
    {
        [DataMember] public double AbsoluteTime;
        [DataMember] public string Caption;
        [DataMember] public string Data;
        [DataMember] public string EventTargetType;
        [DataMember] public string ObjectSequenceNumber;
        [DataMember] public string ObjectStreamID;
        [DataMember] public double Time;
    }
    class DownloadTask
    {
        public string URL;
        public string SavePath;
        public string DisplayName;
    }

    class Program
    {
        static List<DownloadTask> worklist = new List<DownloadTask>();
        static int nextwork = 0;
        static int threadn, finish = 0;
        static object worklock = new object();
        static object threadlock = new object();

        static void go()
        {
            int workn;
            Console.WriteLine("Thread " + Thread.CurrentThread.Name + " started.");
            while (true)
            {
                lock (worklock)
                {
                    workn = nextwork++;
                }
                if (workn < worklist.Count)
                {
                    var work = worklist[workn];
                    Console.WriteLine("Downloading " + work.DisplayName+" ...");
                    bool finished = false;
                    while (!finished)
                    {
                        using (var wc = new WebClient())
                        {
                            try
                            {
                                wc.DownloadFile(work.URL, work.SavePath);
                                finished = true;
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine("Download error: " + e.Message);
                            }
                        }
                        Console.WriteLine("Download " + work.DisplayName + " complete.");
                    }
                }
                else break;
            }
            lock (threadlock)
            {
                finish++;
                Monitor.Pulse(threadlock);
            }
        }
        static void Main(string[] args)
        {
            var folderId = "";
            var regexUUID = new Regex(@"[\da-f]{8}-([\da-f]{4}-){3}[\da-f]{12}");
            var sessions = new List<SessionData>();
            int totalNumber = 0, knownNumber = 0, page = 0;
            var deliveries = new List<DeliveryInfo>();
            //CookieCollection cookies=null;
            do
            {
                Console.Write("Paste the url of a video folder: ");
                folderId = Console.ReadLine();
            } while (!regexUUID.IsMatch(folderId));
            folderId = regexUUID.Match(folderId).Value;
            Console.WriteLine("I found folder ID: " + folderId);
            Console.Write("Getting list of videos...");
            do
            {
                var body = "{\"queryParameters\":{\"query\":null,\"sortColumn\":1,\"sortAscending\":false,\"maxResults\":250,\"page\":"+page+",\"startDate\":null,\"endDate\":null,\"folderID\":\""+folderId+"\",\"bookmarked\":false,\"getFolderData\":true}}";
                var res = HttpPost("http://scs.hosted.panopto.com/Panopto/Services/Data.svc/GetSessions", null, Encoding.UTF8.GetBytes(body), host: "scs.hosted.panopto.com", ContentType: "application/json; charset=UTF-8");
                res = res.Replace("__type", "foobar");
                var ser = new DataContractJsonSerializer(typeof(SessionResults));
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(res)))
                    try
                    {
                        var obj = (SessionResults)ser.ReadObject(ms);
                        totalNumber = obj.d.TotalNumber;
                        foreach (var session in obj.d.Results)
                        {
                            sessions.Add(session);
                            knownNumber++;
                        }
                        Console.Write(" " + knownNumber);
                    }catch(Exception e)
                    {
                        Console.WriteLine();
                        Console.WriteLine("Json read error: " + e.Message);
                        return;
                    }
            } while (knownNumber < totalNumber);
            Console.WriteLine();
            Console.WriteLine("I found " + totalNumber + " videos.");
            Console.Write("Retrieving info of each video...");
            for(int i = 0; i < sessions.Count; i++)
            {
                //if(cookies==null)HttpGet("https://scs.hosted.panopto.com/Panopto/Pages/Viewer.aspx?id=" + sessions[i].DeliveryID,out cookies);
                var body = "deliveryId=" + sessions[i].DeliveryID + "&invocationId=&isLiveNotes=false&refreshAuthCookie=true&isActiveBroadcast=false&responseType=json";
                var res = HttpPost("http://scs.hosted.panopto.com/Panopto/Pages/Viewer/DeliveryInfo.aspx", null, Encoding.UTF8.GetBytes(body), host: "scs.hosted.panopto.com", ContentType: "application/x-www-form-urlencoded; charset=UTF-8");//, Referer: "https://scs.hosted.panopto.com/Panopto/Pages/Viewer.aspx?id="+sessions[i].DeliveryID,Accept: "application/json, text/javascript, */*; q=0.01", cookiesin:cookies);
                var ser = new DataContractJsonSerializer(typeof(DeliveryInfo));
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(res)))
                    try
                    {
                        var obj = (DeliveryInfo)ser.ReadObject(ms);
                        deliveries.Add(obj);
                        Console.Write(" " + (i+1));
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine();
                        Console.WriteLine("Json read error: " + e.Message);
                        return;
                    }
            }
            Console.WriteLine();
            Console.WriteLine("I have info of all videos.\nWhat now?\n1.Just grab all (mp4) videos for me.\n2.Generate a linux script (using wget) for 1.\n3.Get mp4 videos they use on mobile devices.\n4.Generate a linux script (using wget) for 3.");
            int task = InputNumber(1, 4);
            if (task == 1)
            {
                for (int i = 0; i < sessions.Count; i++)
                {
                    var folder1 = safepath(sessions[i].FolderName);
                    var folder2 = folder1+Path.DirectorySeparatorChar+safepath(sessions[i].SessionName);
                    try { Directory.CreateDirectory(folder1); } catch (Exception) { }
                    try { Directory.CreateDirectory(folder2); } catch (Exception) { }
                    int j = 0;
                    foreach (var stream in deliveries[i].Delivery.Streams)
                    {
                        var tag = (stream.Tag == null ? j + "" : stream.Tag);
                        var file = folder2 + Path.DirectorySeparatorChar + tag + "." + findext(stream.StreamHttpUrl);
                        worklist.Add(new DownloadTask() { SavePath = file, URL = stream.StreamHttpUrl, DisplayName=sessions[i].SessionName+"|"+ tag});
                    }
                }
                threadn = InputNumber(1, 2, "Enter number of threads (1-2): "); // They seem to only allow 2 download threads.
                for (int i = 0; i < threadn; i++)
                {
                    var thread = new Thread(new ThreadStart(go));
                    thread.Name = "" + (i+1);
                    thread.Start();
                }
                lock (threadlock)
                {
                    while (finish != threadn)
                    {
                        Monitor.Wait(threadlock);
                    }
                }
                Console.WriteLine("All complete.");
            }else if (task == 2)
            {
                using (var sw = new StreamWriter("download.sh", false, Encoding.UTF8)) {
                    sw.WriteLine("#!/bin/sh");
                    for (int i = 0; i < sessions.Count; i++)
                    {
                        var folder1 = safepath(sessions[i].FolderName);
                        var folder2 = folder1 + Path.DirectorySeparatorChar + safepath(sessions[i].SessionName);
                        sw.WriteLine("mkdir -p \"" + folder2 + "\"");
                        int j = 0;
                        foreach (var stream in deliveries[i].Delivery.Streams)
                        {
                            var tag = (stream.Tag == null ? j + "" : stream.Tag);
                            var file = folder2 + Path.DirectorySeparatorChar + tag + "." + findext(stream.StreamHttpUrl);
                            sw.WriteLine("wget -c -O \"" + file + "\" \"" + stream.StreamHttpUrl + "\"");
                        }
                    }
                }
                Console.WriteLine("Check out download.sh in the program folder.");
            }else if (task == 3)
            {
                for (int i = 0; i < sessions.Count; i++)
                {
                    var folder1 = safepath(sessions[i].FolderName);
                    var url = "https://scs.hosted.panopto.com/Panopto/Podcast/Embed/" + sessions[i].SessionID + ".mp4";
                    try { Directory.CreateDirectory(folder1); } catch (Exception) { }
                    var file = folder1 + Path.DirectorySeparatorChar + safepath(sessions[i].SessionName) + ".mp4";
                    worklist.Add(new DownloadTask() { SavePath = file, URL = url, DisplayName = sessions[i].SessionName });
                }
                threadn = InputNumber(1, 2, "Enter number of threads (1-2): "); // They seem to only allow 2 download threads.
                for (int i = 0; i < threadn; i++)
                {
                    var thread = new Thread(new ThreadStart(go));
                    thread.Name = "" + (i + 1);
                    thread.Start();
                }
                lock (threadlock)
                {
                    while (finish != threadn)
                    {
                        Monitor.Wait(threadlock);
                    }
                }
                Console.WriteLine("All complete.");
            }else if (task == 4)
            {
                using (var sw = new StreamWriter("download.sh", false, Encoding.UTF8))
                {
                    sw.WriteLine("#!/bin/sh");
                    for (int i = 0; i < sessions.Count; i++)
                    {
                        var folder1 = safepath(sessions[i].FolderName);
                        sw.WriteLine("mkdir \"" + folder1 + "\"");
                        var url = "https://scs.hosted.panopto.com/Panopto/Podcast/Embed/" + sessions[i].SessionID + ".mp4";
                        var file = folder1 + Path.DirectorySeparatorChar + safepath(sessions[i].SessionName) + ".mp4";
                        sw.WriteLine("wget -c -O \"" + file + "\" \"" + url + "\"");
                    }
                }
                Console.WriteLine("Check out download.sh in the program folder.");
            }
        }
        static string findext(string s)
        {
            s = s.Split('?')[0];
            s = s.Split(new char[] { '.', '/' }).Last();
            return s;
        }
        static string safepath(string s)
        {
            foreach (var c in Path.GetInvalidPathChars().Union(Path.GetInvalidFileNameChars()))
            {
                s = s.Replace(c, ' ');
            }
            return s;
        }
        static int InputNumber(int min,int max,string prompt="Enter choice: ")
        {
            int choice = min - 1;
            while (choice < min || choice > max)
            {
                Console.Write(prompt);
                var ts = Console.ReadLine();
                int ti = 0;
                if (int.TryParse(ts, out ti)) choice = ti;
            }
            return choice;
        }
        public static string GetResponse(ref HttpWebRequest req,out CookieCollection cookies, bool GetLocation = false, bool GetRange = false, bool NeedResponse = true)
        {
            HttpWebResponse res = null;
            cookies = null;
            try
            {
                res = (HttpWebResponse)req.GetResponse();
                cookies = res.Cookies;
            }
            catch (WebException e)
            {
                StreamReader ereader = new StreamReader(e.Response.GetResponseStream(), Encoding.GetEncoding("utf-8"));
                string erespHTML = ereader.ReadToEnd();
                Console.WriteLine(erespHTML);
                throw new Exception(erespHTML);
            }
            if (GetLocation)
            {
                string ts = res.Headers["Location"];
                res.Close();
                Console.WriteLine("Location: " + ts);
                return ts;
            }
            if (GetRange && res.ContentLength == 0)
            {
                string ts = res.Headers["Range"];
                Console.WriteLine("Range: " + ts);
                return ts;
            }
            if (NeedResponse)
            {
                StreamReader reader = new StreamReader(res.GetResponseStream(), Encoding.GetEncoding("utf-8"));
                string respHTML = reader.ReadToEnd();
                res.Close();
                //Console.WriteLine(respHTML);
                return respHTML;
            }
            else
            {
                res.Close();
                return "";
            }
        }
        public static string GetResponse(ref HttpWebRequest req, bool GetLocation = false, bool GetRange = false, bool NeedResponse = true)
        {
            CookieCollection c;
            return GetResponse(ref req, out c, GetLocation, GetRange, NeedResponse);
        }
        public static HttpWebRequest GenerateRequest(string URL, string Method, string token, bool KeepAlive = false, string ContentType = null, byte[] data = null, int offset = 0, int length = 0, string ContentRange = null, bool PreferAsync = false, int Timeout = 20 * 1000, string host = null, string Referer = null, string Accept = null, CookieCollection cookies=null)
        {
            Uri httpUrl = new Uri(URL);
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(httpUrl);
            req.ProtocolVersion = new System.Version("1.0");
            req.Timeout = Timeout;
            req.ReadWriteTimeout = Timeout;
            req.Method = Method;
            if (token != null) req.Headers.Add("Authorization", "Bearer " + token);
            req.KeepAlive = KeepAlive;
            if (ContentType != null) req.ContentType = ContentType;
            if (ContentRange != null) req.Headers.Add("Content-Range", ContentRange);
            if (PreferAsync == true) req.Headers.Add("Prefer", "respond-async");
            if (Referer != null) req.Referer = Referer;
            if (Accept != null) req.Accept = Accept;
            if (cookies != null)
            {
                req.CookieContainer = new CookieContainer();
                req.CookieContainer.Add(cookies);
            }
            if (data != null)
            {
                req.ContentLength = length;
                Stream stream = req.GetRequestStream();
                stream.Write(data, offset, length);
                stream.Close();
            }
            return req;
        }
        public static string HttpGet(string URL, string token = null, bool GetLocation = false, bool AllowAutoRedirect = true, bool NeedResponse = true, int Timeout = 5 * 1000, string host = null)
        {
            HttpWebRequest req = GenerateRequest(URL, "GET", token, false, null, null, 0, 0, null, false, Timeout, host);
            if (AllowAutoRedirect == false) req.AllowAutoRedirect = false;
            return GetResponse(ref req, GetLocation, false, NeedResponse);
        }
        public static string HttpGet(string URL,out CookieCollection cookies, string token = null, bool GetLocation = false, bool AllowAutoRedirect = true, bool NeedResponse = true, int Timeout = 5 * 1000, string host = null)
        {
            HttpWebRequest req = GenerateRequest(URL, "GET", token, false, null, null, 0, 0, null, false, Timeout, host);
            if (AllowAutoRedirect == false) req.AllowAutoRedirect = false;
            return GetResponse(ref req,out cookies, GetLocation, false, NeedResponse);
        }
        public static string HttpPost(string URL, string token, byte[] data, int offset = 0, int length = -1, bool NeedResponse = true, int Timeout = 20 * 1000, string host = null, string ContentType=null, string Referer=null,string Accept=null, CookieCollection cookiesin = null)
        {
            if (length == -1) length = data.Length;
            HttpWebRequest req = GenerateRequest(URL, "POST", token, false,ContentType, data, 0, data.Length, null, false, Timeout, host,Referer,Accept,cookiesin);
            return GetResponse(ref req, false, false, NeedResponse);
        }
        public static string HttpPost(string URL, string token, byte[] data, out CookieCollection cookies, int offset = 0, int length = -1, bool NeedResponse = true, int Timeout = 20 * 1000, string host = null, string ContentType = null, string Referer = null, string Accept = null, CookieCollection cookiesin=null)
        {
            if (length == -1) length = data.Length;
            HttpWebRequest req = GenerateRequest(URL, "POST", token, false, ContentType, data, 0, data.Length, null, false, Timeout, host, Referer, Accept,cookiesin);
            return GetResponse(ref req, out cookies, false, false, NeedResponse);
        }
    }
}
