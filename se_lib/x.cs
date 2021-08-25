using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace se_lib {

    [Serializable]
    public class XObj {
        public string code { get; set; }
        public string[] refs { get; set; }
    }

    class X {
        public static XObj FromXml(byte[] data) {
            using (var sr = new System.IO.StringReader(Encoding.UTF8.GetString(data))) {
                var xml = new System.Xml.Serialization.XmlSerializer(typeof(XObj));
                return xml.Deserialize(sr) as XObj;
            }
        }
    }
}
