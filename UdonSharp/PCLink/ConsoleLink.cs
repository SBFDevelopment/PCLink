using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

namespace com.SBFDevelopment.PCLink
{
    public enum UrlRequestType
    {
        GET,
        HEAD,
        POST,
        PUT,
        DELETE,
        CONNECT,
        OPTIONS,
        TRACE,
        PATCH,
        UNKNOWN
    }
    
    public class ConsoleLink : UdonSharpBehaviour
    {
        public void SendJsonData(string json, NetworkEventTarget eventTarget = NetworkEventTarget.Owner)
        {
            var jsonData = new DataDictionary();
            if (VRCJson.TryDeserializeFromJson(json, out var token))
            {
                jsonData.Add("Data", token);
            }
            
            if (VRCJson.TrySerializeToJson(jsonData, JsonExportType.Minify, out var jsonToken))
            {
                SendData("PCLink.JSON", jsonToken.String, eventTarget);
            }
        }
        
        public void SendWebRequest(string url, UrlRequestType requestType, string header = "",
            string body = "", NetworkEventTarget eventTarget = NetworkEventTarget.Owner)
        {
            var jsonData = new DataDictionary();
            jsonData.Add("URL", url);
            jsonData.Add("RequestType", requestType.ToString());
            
            if (VRCJson.TryDeserializeFromJson(header, out var headerData))
            {
                jsonData.Add("Header", headerData);
            }

            if (VRCJson.TryDeserializeFromJson(body, out var bodyData))
            {
                jsonData.Add("Body", bodyData);
            }
            
            if (VRCJson.TrySerializeToJson(jsonData, JsonExportType.Minify, out var jsonToken))
            {
                SendData("PCLink.URL", jsonToken.String, eventTarget);
            }
        }
        
        public void SendData(string requestType, string data, NetworkEventTarget eventTarget = NetworkEventTarget.Owner)
        {
            SendCustomNetworkEvent(eventTarget, nameof(_SendData), requestType, data);
        }
        
        [NetworkCallable]
        public void _SendData(string requestType, string data)
        {
            Debug.Log($"{{{requestType}}}: {data}");
        }
    }
}
