using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
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
        [DataMember] public int AverageRating;
        [DataMember] public string DeliveryID;
        [DataMember] public double Duration;
        [DataMember] public string SessionID;
        [DataMember] public string SessionName;
        [DataMember] public string StartTime;
        [DataMember] public string ThumbUrl;
    }


    class Program
    {
        static void Main(string[] args)
        {
            var folderId = "";
            var regexUUID = new Regex(@"[\da-f]{8}-([\da-f]{4}-){3}[\da-f]{12}");
            var sessions = new List<SessionData>();
            int totalNumber = 0, knownNumber = 0, page = 0;
            do
            {
                Console.WriteLine("Paste the url of a video folder:");
                folderId = Console.ReadLine();
            } while (!regexUUID.IsMatch(folderId));
            folderId = regexUUID.Match(folderId).Value;
            Console.WriteLine("I found folder ID: " + folderId);
            Console.Write("Getting list of videos...");
            do
            {
                var body = "{\"queryParameters\":{\"query\":null,\"sortColumn\":1,\"sortAscending\":false,\"maxResults\":250,\"page\":"+page+",\"startDate\":null,\"endDate\":null,\"folderID\":\""+folderId+"\",\"bookmarked\":false,\"getFolderData\":true}}";
                var res = HttpPost("http://scs.hosted.panopto.com/Panopto/Services/Data.svc/GetSessions", null, Encoding.UTF8.GetBytes(body), host: "scs.hosted.panopto.com");
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
                        Console.Write("" + knownNumber);
                    }catch(Exception e)
                    {
                        Console.WriteLine();
                        Console.WriteLine("Json read error: " + e.ToString());
                        return;
                    }
            } while (knownNumber < totalNumber);
            Console.WriteLine();
            Console.WriteLine("I found " + totalNumber + " videos.");
            
        }
        public static string GetResponse(ref HttpWebRequest req, bool GetLocation = false, bool GetRange = false, bool NeedResponse = true)
        {
            HttpWebResponse res = null;
            try
            {
                res = (HttpWebResponse)req.GetResponse();
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
        public static HttpWebRequest GenerateRequest(string URL, string Method, string token, bool KeepAlive = false, string ContentType = null, byte[] data = null, int offset = 0, int length = 0, string ContentRange = null, bool PreferAsync = false, int Timeout = 20 * 1000, string host = null)
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
        public static string HttpPost(string URL, string token, byte[] data, int offset = 0, int length = -1, bool NeedResponse = true, int Timeout = 20 * 1000, string host = null)
        {
            if (length == -1) length = data.Length;
            HttpWebRequest req = GenerateRequest(URL, "POST", token, false, "application/json; charset=UTF-8", data, 0, data.Length, null, false, Timeout, host);
            return GetResponse(ref req, false, false, NeedResponse);
        }
    }
}
