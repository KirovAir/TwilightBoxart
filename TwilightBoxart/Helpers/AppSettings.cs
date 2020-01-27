using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;

namespace KirovAir.Core.Config
{
    /// <summary>
    /// Base class for key/value application/web config files. Inherit this class and add config properties where needed.
    /// </summary>
    public abstract class AppSettings
    {
        /// <summary>
        /// Appends the name/value collection on to the currently initialized config class properties.
        /// </summary>
        /// <param name="values"></param>
        public void Load(NameValueCollection values)
        {
            var properties = GetType().GetProperties();

            foreach (var keyName in values.AllKeys)
            {
                var property = properties.FirstOrDefault(c => c.Name.ToLower() == keyName.ToLower());
                if (property == null)
                    continue;

                var value = values[keyName];

                try
                {
                    if (property.PropertyType == typeof(string))
                    {
                        property.SetValue(this, value);
                    }
                    else if (property.PropertyType == typeof(int))
                    {
                        property.SetValue(this, Convert.ToInt32(value));
                    }
                    else if (property.PropertyType == typeof(uint))
                    {
                        uint parsed;
                        if (value.StartsWith("0x", StringComparison.CurrentCultureIgnoreCase))
                        {
                            value = value.Substring(2);
                            parsed = uint.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                        }
                        else
                        {
                            parsed = Convert.ToUInt32(value);
                        }

                        property.SetValue(this, parsed);
                    }
                    else if (property.PropertyType == typeof(bool))
                    {
                        property.SetValue(this, Convert.ToBoolean(value));
                    }
                    else if (property.PropertyType == typeof(DateTime))
                    {
                        property.SetValue(this, DateTime.Parse(value));
                    }
                    else if (property.PropertyType == typeof(List<string>))
                    {
                        var list = new List<string>();
                        if (!string.IsNullOrEmpty(value))
                        {
                            list = value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(c => c.Trim()).ToList();
                        }

                        property.SetValue(this, list);
                    }
                    else if (property.PropertyType.BaseType == typeof(Enum))
                    {
                        property.SetValue(this, Enum.Parse(property.PropertyType, value, true));
                    }
                }
                catch (Exception e)
                {
                    throw new Exception($"Unable not load app setting: {keyName} with value {value}. Is the property type correct?", e);
                }
            }
        }
    }
}