using Seq.Apps;
using Seq.Apps.LogEvents;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;
using Serilog;

namespace Seq.App.Exporter
{
    [SeqApp("Event Exporter",
      Description = "Exports events to custom endpoints")]
    public class EventExporter : Reactor, ISubscribeTo<LogEventData>
    {
        [SeqAppSetting(
           DisplayName = "Posting URL",
           HelpText = "The location where the event should be exported. ex http://www.site.com/endpointname",
           InputType = SettingInputType.Text)]
        public string PostingURL { get; set; }

        [SeqAppSetting(
          DisplayName = "Allowed Environment(s)",
          HelpText = "The environment(s) which is allowed to use this app. (leave blank for everything, or key=value,value   ex: Environment=Website1,Website2 or Application=Website1)",
          IsOptional = true,
          InputType = SettingInputType.Text)]
        public string AllowedEnvironments
        {
            get
            {
                return _allowedEnvironments;
            }
            set
            {
                _allowedEnvironments = value;

                if (!string.IsNullOrWhiteSpace(_allowedEnvironments)) //do we have a value at all?
                {
                    if (_allowedEnvironments.IndexOf("=") > -1) //do we have a seperator?
                    {
                        string[] initialSetup = _allowedEnvironments.Split(new char[] { '=' });

                        //get the key
                        _allowedEnvironmentsKey = initialSetup[0];
                        //get the values
                        _allowedEnvironmentsValues = initialSetup[1].Split(new char[] { ',' }).ToList();
                    }
                }
            }
        }
        private string _allowedEnvironments;
        private string _allowedEnvironmentsKey;
        private List<string> _allowedEnvironmentsValues = new List<string>();

        [SeqAppSetting(
          DisplayName = "Fields to export",
          HelpText = "Fields that should be exported (comma seperated). ex EventID, Environment, Routine",
          InputType = SettingInputType.Text)]
        public string ExportFields
        {
            get { return _exportFields; }
            set
            {
                _exportFields = value;

                if (!string.IsNullOrWhiteSpace(value))
                {
                    ExportFieldsList.Clear();
                    string[] splits = value.Split(new char[] { ',' });
                    foreach (string s in splits)
                    {
                        ExportFieldsList.Add(s.Replace(" ", ""));
                    }
                }

            }
        }
        private string _exportFields;
        private List<string> ExportFieldsList = new List<string>();

        [SeqAppSetting(
       DisplayName = "Send the property value exactly as is",
       HelpText = "If this is selected, the value of the property will be sent exactly as it appears in the log without being serialized. This option will only work if there are only one property in the 'Fields to export' list",
       InputType = SettingInputType.Checkbox)]
        public bool DoNotSerialize { get; set; }


        public void On(Event<LogEventData> evt)
        {
            //get the properties from the event that you selected
            var evtProperties = (IDictionary<string, object>)ToDynamic(evt.Data.Properties ?? new Dictionary<string, object>());

            bool allowed = IsEnvironmentAllowed(evtProperties);
            if (!allowed)
            {
                throw new Exception("App is not allowed to run for this event.");
            }

            //Based on your setup, create a serializable key/value pair (key = property from setup, value = property value from log)
            var results = GetExportValues(evtProperties);

            //create a JSON string from the object
            string serializedObject = new JavaScriptSerializer().Serialize(results);

            if (DoNotSerialize && ExportFieldsList.Count == 1)
            {
                serializedObject = results[ExportFieldsList[0]].ToString();
            }

            Log.Information("{@SerializedObject}", serializedObject);

            //Submit the Object to whatever url the user specified.
            string response = PostToWebservice(serializedObject);

            Log.Information("{@Response}", response);
        }

        private bool IsEnvironmentAllowed(IDictionary<string, object> eventProperties)
        {
            object value = null;
            bool allowed = true; //start of allowing the environment

            if (!string.IsNullOrWhiteSpace(_allowedEnvironmentsKey))
            {
                allowed = false;  //setup key is found, assume it is not allowed and then switch to allowed if applicable

                eventProperties.TryGetValue(_allowedEnvironmentsKey.ToUpper(), out value); //get the value that matches the setup key provided by the user     

                if (value != null)
                {
                    //there is environment setup found, check if this app is allowed
                    foreach (string environment in _allowedEnvironmentsValues)
                    {
                        if (environment.Trim() == value.ToString().Trim())
                        {
                            allowed = true;
                        }
                    }
                }
            }

            return allowed;
        }

        private Dictionary<string, object> GetExportValues(IDictionary<string, object> eventProperties)
        {
            object value = null;

            //based on your setup, create a serializable key/value pair (key = property from setup, value = property value from log)
            Dictionary<string, object> results = new Dictionary<string, object>();
            foreach (string property in ExportFieldsList)
            {
                eventProperties.TryGetValue(property.ToUpper(), out value);
                if (value != null)
                {
                    results.Add(property, value.ToString());
                }
            }

            return results;
        }

        public string PostToWebservice(string serializedObject)
        {
            Uri url = new Uri(PostingURL);

            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Accept = "application/json";
            request.ContentType = "application/json";
            request.Method = "POST";

            ASCIIEncoding encoding = new ASCIIEncoding();
            Byte[] bytes = encoding.GetBytes(serializedObject);

            string responseStream = "";
            using (Stream newStream = request.GetRequestStream())
            {
                newStream.Write(bytes, 0, bytes.Length);
                newStream.Close();

                using (WebResponse response = request.GetResponse())
                {
                    using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                    {
                        responseStream = reader.ReadToEnd().Trim();
                    }
                }
            }

            return responseStream;
        }

        static object ToDynamic(object o)
        {
            var dictionary = o as IEnumerable<KeyValuePair<string, object>>;
            if (dictionary != null)
            {
                var result = new ExpandoObject();
                var asDict = (IDictionary<string, object>)result;
                foreach (var kvp in dictionary)
                    asDict.Add(kvp.Key.ToUpper(), ToDynamic(kvp.Value));
                return result;
            }

            var enumerable = o as IEnumerable<object>;
            if (enumerable != null)
            {
                return enumerable.Select(ToDynamic).ToArray();
            }

            return o;
        }
    }
}
