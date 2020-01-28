using System;
using System.ComponentModel;

namespace TwilightBoxart.Helpers
{
    public static class EnumEx
    {
        public static string GetDescription<T>(this T enumVal) where T : struct
        {
            var type = enumVal.GetType();
            if (!type.IsEnum)
            {
                throw new ArgumentException("EnumerationValue must be of Enum type", "enumVal");
            }

            //Tries to find a DescriptionAttribute for a potential friendly name
            //for the enum
            var memberInfo = type.GetMember(enumVal.ToString());
            if (memberInfo.Length > 0)
            {
                var attrs = memberInfo[0].GetCustomAttributes(typeof(DescriptionAttribute), false);

                if (attrs.Length > 0)
                {
                    //Pull out the description value
                    return ((DescriptionAttribute) attrs[0]).Description;
                }
            }

            //If we have no description attribute, just return the ToString of the enum
            return enumVal.ToString().UpperToSpace();
        }

        public static T GetEnum<T>(this string value) where T : struct
        {
            var enumType = typeof(T);

            //check and see if the value is a non attribute value
            var found = false;

            if (!Enum.TryParse<T>(value, out var theEnum))
            {
                foreach (T enumValue in Enum.GetValues(enumType))
                {
                    var field = enumType.GetField(enumValue.ToString());

                    if (!(Attribute.GetCustomAttribute(field,
                            typeof(DescriptionAttribute)) is DescriptionAttribute attr) || !attr.Description.Equals(value)) continue;

                    theEnum = enumValue;
                    found = true;
                    break;
                }

                if (!found)
                    throw new ArgumentException("Cannot convert " + value + " to " + enumType);
            }

            return theEnum;
        }
    }
}