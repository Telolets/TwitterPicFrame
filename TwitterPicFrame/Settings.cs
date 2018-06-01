using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Configuration;

namespace TwitterPicFrame
{
    public class Settings
    {
        public static String GetValueFromConfig(string key)
        {
            String result = ConfigurationManager.AppSettings[key];
            if (String.IsNullOrEmpty(result))
                return null;
            else
                return result;
        }
    }
}
