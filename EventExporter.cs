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

namespace Seq.App.Event.Exporter
{
    [SeqApp("Event Exporter",
      Description = "Exports events to custom endpoints")]
    public class SummitLogger : Reactor, ISubscribeTo<LogEventData>
    {
        [SeqAppSetting(
           DisplayName = "Posting URL",
           HelpText = "The location where the event should be exported.",
           InputType = SettingInputType.Text)]
        public string PostingURL { get; set; }

        [SeqAppSetting(
          DisplayName = "Allowed Environment(s)",
          HelpText = "The environment(s) which is allowed to use this app. (leave blank for everything, or key=value,value like for example: Environment=Website1,Website2 or Application=Website1)",
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
        private string _allowedEnvironmentsKey = "N/A";
        private List<string> _allowedEnvironmentsValues = new List<string>();

        [SeqAppSetting(
          DisplayName = "Fields to export",
          HelpText = "Fields that should be exported (comma seperated).",
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
            Dictionary<string, object> results = GetExportValues(evtProperties);

            //create a JSON string from the object
            string serializedObject = new JavaScriptSerializer().Serialize(results);

            //Submit the Object to whatever url the user specified.
            WebResponse response = PostToWebservice(serializedObject);
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

        private WebResponse PostToWebservice(string serializedObject)
        {
            Uri url = new Uri(PostingURL);

            var http = (HttpWebRequest)WebRequest.Create(url);
            http.Accept = "application/json";
            http.ContentType = "application/json";
            http.Method = "POST";

            ASCIIEncoding encoding = new ASCIIEncoding();
            Byte[] bytes = encoding.GetBytes(serializedObject);

            Stream newStream = http.GetRequestStream();
            newStream.Write(bytes, 0, bytes.Length);
            newStream.Close();

            return http.GetResponse();
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
