using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using Newtonsoft.Json.Linq;

namespace ConsoleApp1 {
    public sealed partial class HttpRequestUtil {
        private static HttpRequestUtil? _httpRequestUtil;
        private CookieCollection _cookies = CookieJar.GetCookies();
        private HttpWebResponse _res;
        public List<string> dirPath;
        public static HttpRequestUtil GetInstance() {
            return _httpRequestUtil ??= new HttpRequestUtil();
        }

        private HttpRequestUtil() => GetSession();

        private HttpWebResponse SendReq(string url, bool isPost, byte[] postData, 
            bool allowAutoRedirect = true, string? contentType = null) {
            try {
                var req = (HttpWebRequest)WebRequest.Create(url);
                CookieContainer cookieContainer = new();
                req.CookieContainer = cookieContainer;
                foreach (Cookie cookie in _cookies) req.CookieContainer.Add(cookie);

                req.Method = isPost ? "POST" : "GET";
                req.AllowAutoRedirect = allowAutoRedirect;
                req.UserAgent = Constants.Http.UserAgent;
                req.Headers.Add("Accept",  Constants.Http.Accept);
                req.Headers.Add("Referer", Constants.Http.Referer);

                if (isPost) {
                    req.Headers.Add("X-Requested-With", "XMLHttpRequest");
                    req.ContentType = string.IsNullOrEmpty(contentType)
                        ? "application/x-www-form-urlencoded; charset=UTF-8"
                        : contentType;
                    req.ContentLength = postData.Length;
                    using var writer = req.GetRequestStream();
                    writer.Write(postData, 0, postData.Length);
                }
                else {
                    req.Headers.Add("Cache-Control", "no-cache");
                    if (!string.IsNullOrEmpty(contentType)) req.ContentType = contentType;
                    req.ContentLength = 0;
                }

                _res = (HttpWebResponse)req.GetResponse();
                _cookies = CookieJar.MergeCookies(_cookies, _res.Cookies);
                return _res;
            }
            catch (WebException ex) {
                Debug.Print("InternetUtils - isServerOnline - " + ex.Status);
                throw ex;
            }
        }

        public string GetIdByNameFromDirList(string name) {
            try {
                using var stream = new StreamReader(_res.GetResponseStream());
                var json = JObject.Parse(stream.ReadToEnd());
                var match = json["data"]["gridRoot"].Values<JObject>()?
                    .FirstOrDefault(m => m["NAME"].Value<string>() == name);
                return match != null ? match["ID"].ToString() : string.Empty;
            }
            catch {
                return string.Empty;
            }
        }

        public HttpRequestUtil GetDirList(string id) {
            const string url = Constants.Http.BaseUrl + "/NltGetNodeList.do";
            _res = SendReq(url, true,
                Encoding.UTF8.GetBytes("getNodeList=true&nodeId=" + id +
                                       "&start=0&limit=100&sort=Name&dir=ASC"), false);
            return this;
        }


        private HttpRequestUtil GetSession()
        {
            const string url = Constants.Http.BaseUrl; //+ "/jsp/index.jsp";
            _res = SendReq(url, false, null);
            return this;
        }

        public string GetFileSequenceNo(string id) {
            const string url = Constants.Http.BaseUrl + "/hisShowHistoriesJson.do";
            _res = SendReq(url, false, null);
            using var stream = new StreamReader(_res.GetResponseStream());
            var json = JObject.Parse(stream.ReadToEnd());
            Console.WriteLine(stream.ReadToEnd());
            var seq = json["data"]["gridRoot"][0]["SEQUENCE"].ToString();
            return seq;
        }

        public HttpRequestUtil GetNodeInformation(string id) {
            const string url = Constants.Http.BaseUrl + "/showNodeInformation.do";
            _res = SendReq(url, true,
                Encoding.UTF8.GetBytes("getNodeProperties=true&nodeId=" + id), false);
            return this;
        }


        public bool DownloadFile(string id, string seq, string dir) {
            var url = Constants.Http.BaseUrl + "/cmnDownloadFile.do?" + "nodeId" + id + "&seq=" + seq;
            _res = SendReq(url, false, null, false);
            var regex = Regex();
            var matches = regex.Matches(_res.Headers["Content-Disposition"]);
            var fileName = HttpUtility.UrlDecode(matches[0].Groups[1].Value);
            CreateDirectory(dir);
            var destPath = dir + "\\" + fileName;
            var readStream = new BinaryReader(_res.GetResponseStream());
            var byteBucket = new byte[_res.ContentLength];
            var done = false;
            using var fs = new FileStream(destPath, FileMode.CreateNew, FileAccess.Write);
            var totalBytesRead = 0;

            while (!done) {
                var currentBytesRead = readStream.Read(byteBucket, 0, 
                    Convert.ToInt32(_res.ContentLength));
                fs.Write(byteBucket, 0, currentBytesRead);
                totalBytesRead += currentBytesRead;
                if (totalBytesRead == _res.ContentLength) done = true;
            }
            return done;
        }

        private static void CreateDirectory(string path) {
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        }

        public JObject ToJson() => JObject.Parse(new StreamReader(_res.GetResponseStream()).ReadToEnd());
        
        [GeneratedRegex(".*filename,*?=utf-8\\'\\'(.+)", RegexOptions.Compiled)]
        private static partial Regex Regex();
    }
    

    public static class JObjectExtensions {
        public static void RecursiveSearchFromJson(this JObject json) {

            var req = HttpRequestUtil.GetInstance();
            foreach (var f in json["data"]["gridRoot"].AsEnumerable())
                switch (f["NODETYPE"].ToString()) {
                    case "SHORTCUT": {
                        var id = f["SCT_DESTINATIONID"].ToString();
                        var ni = req.GetNodeInformation(id).ToJson();
                        var nt = ni["data"]["nodeInformation"][0]["nodeTypeCat"].ToString();
                        switch (nt) {
                            case "10": {
                                var nodeName = ni["data"]["nodeInformation"][0]["nodeName"].ToString();
                                req.dirPath.Add(nodeName);
                                req.GetDirList(id).ToJson().RecursiveSearchFromJson();
                                req.dirPath.RemoveAt(req.dirPath.Count - 1);
                                break;
                            }
                            case "11": {
                                var seq = req.GetFileSequenceNo(id);
                                if (seq != "") {
                                    req.DownloadFile(id, seq,Path.Combine(req.dirPath.ToArray()));
                                }
                                else {
                                    Console.WriteLine("error: append download list => " + id);
                                }
                                break;
                            }
                        }
                        break;
                    }
                    case "FILE": {
                        if (Convert.ToInt32(f["FILE_CNT"]) > 1)
                            Console.WriteLine("WARNING: " + f["ID"] + " FILE COUNT=> " + f["FILE_CNT"]);
                        req.DownloadFile(f["ID"].ToString(), f["SEQUENCE"].ToString(),
                            Path.Combine(
                                req.dirPath.ToArray()));
                        break;
                    }
                    case "FOLDER": {
                        var nodeName = req.GetNodeInformation(f["ID"].ToString())
                                .ToJson()["data"]["nodeInformation"][0]["nodeName"].ToString();
                        req.dirPath.Add(nodeName);
                        req.GetDirList(f["ID"].ToString()).ToJson().RecursiveSearchFromJson();
                        req.dirPath.RemoveAt(req.dirPath.Count - 1);
                        break;
                    }
                    default:
                        Console.WriteLine("unknown" + f["ID"]);
                        break;
                }
        }
    }
    
}