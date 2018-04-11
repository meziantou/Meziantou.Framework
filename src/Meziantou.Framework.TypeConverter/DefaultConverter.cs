using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Meziantou.Framework.Utilities
{
    public class DefaultConverter : IConverter
    {
        private const string HexaChars = "0123456789ABCDEF";
        private static readonly MethodInfo EnumTryParseMethodInfo = GetEnumTryParseMethodInfo();

        private static MethodInfo GetEnumTryParseMethodInfo()
        {
            // Enum.TryParse<T>(string value, bool ignoreCase, out T value)
            return typeof(Enum)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == nameof(Enum.TryParse) && m.IsGenericMethod && m.GetParameters().Length == 3);
        }

        public virtual bool TryChangeType(object input, Type conversionType, IFormatProvider provider, out object value)
        {
            return TryConvert(input, conversionType, provider, out value);
        }

        private static byte GetHexaByte(char c)
        {
            if ((c >= '0') && (c <= '9'))
                return (byte)(c - '0');

            if ((c >= 'A') && (c <= 'F'))
                return (byte)(c - 'A' + 10);

            if ((c >= 'a') && (c <= 'f'))
                return (byte)(c - 'a' + 10);

            return 0xFF;
        }

        private static bool NormalizeHexString(ref string s)
        {
            if (s == null)
                return false;

            if (s.Length > 0)
            {
                if (s[0] == 'x' || s[0] == 'X')
                {
                    s = s.Substring(1);
                    return true;
                }

                if (s.Length > 1)
                {
                    if (s[0] == '0' && (s[1] == 'x' || s[1] == 'X'))
                    {
                        s = s.Substring(2);
                        return true;
                    }
                }
            }
            return false;
        }

        private static void GetBytes(decimal d, IList<byte> buffer)
        {
            var ints = decimal.GetBits(d);
            buffer[0] = (byte)ints[0];
            buffer[1] = (byte)(ints[0] >> 8);
            buffer[2] = (byte)(ints[0] >> 0x10);
            buffer[3] = (byte)(ints[0] >> 0x18);
            buffer[4] = (byte)ints[1];
            buffer[5] = (byte)(ints[1] >> 8);
            buffer[6] = (byte)(ints[1] >> 0x10);
            buffer[7] = (byte)(ints[1] >> 0x18);
            buffer[8] = (byte)ints[2];
            buffer[9] = (byte)(ints[2] >> 8);
            buffer[10] = (byte)(ints[2] >> 0x10);
            buffer[11] = (byte)(ints[2] >> 0x18);
            buffer[12] = (byte)ints[3];
            buffer[13] = (byte)(ints[3] >> 8);
            buffer[14] = (byte)(ints[3] >> 0x10);
            buffer[15] = (byte)(ints[3] >> 0x18);
        }

        private static bool IsNumberType(Type type)
        {
            if (type == null)
                return false;

            return type == typeof(int) || type == typeof(long) || type == typeof(short) ||
                   type == typeof(uint) || type == typeof(ulong) || type == typeof(ushort) ||
                   type == typeof(bool) || type == typeof(double) || type == typeof(float) ||
                   type == typeof(decimal) || type == typeof(byte) || type == typeof(sbyte);
        }

        private static bool IsNullOrEmptyString(object input)
        {
            if (input == null)
                return true;

            if (input is string s)
                return string.IsNullOrWhiteSpace(s);

            return false;
        }

        private static bool EnumTryParse(Type type, string input, out object value)
        {
            var mi = EnumTryParseMethodInfo.MakeGenericMethod(type);
            object[] args = { input, true, Enum.ToObject(type, 0) };
            var b = (bool)mi.Invoke(null, args);
            value = args[2];
            return b;
        }

        public static string ToHexa(byte[] bytes)
        {
            if (bytes == null)
                return null;

            return ToHexa(bytes, 0, bytes.Length);
        }

        public static string ToHexa(byte[] bytes, int offset, int count)
        {
            if (bytes == null)
                return string.Empty;

            if (offset < 0)
                throw new ArgumentException(null, nameof(offset));

            if (count < 0)
                throw new ArgumentException(null, nameof(count));

            if (offset >= bytes.Length)
                return string.Empty;

            count = Math.Min(count, bytes.Length - offset);

            var sb = new StringBuilder(count * 2);
            for (var i = offset; i < offset + count; i++)
            {
                sb.Append(HexaChars[bytes[i] / 16]);
                sb.Append(HexaChars[bytes[i] % 16]);
            }
            return sb.ToString();
        }

        public static byte[] FromHexa(string hex)
        {
            var list = new List<byte>();
            var lo = false;
            byte prev = 0;
            int offset;

            // handle 0x or 0X notation
            if (hex.Length >= 2 && hex[0] == '0' && (hex[1] == 'x' || hex[1] == 'X'))
            {
                offset = 2;
            }
            else
            {
                offset = 0;
            }

            for (var i = 0; i < hex.Length - offset; i++)
            {
                var b = GetHexaByte(hex[i + offset]);
                if (b == 0xFF)
                {
                    return null;
                }

                if (lo)
                {
                    list.Add((byte)(prev * 16 + b));
                }
                else
                {
                    prev = b;
                }
                lo = !lo;
            }

            return list.ToArray();
        }

        protected virtual bool TryConvert(byte[] input, IFormatProvider provider, out string value)
        {
            value = Convert.ToBase64String(input);
            return true;
        }

        protected virtual bool TryConvert(TimeSpan input, IFormatProvider provider, out byte[] value)
        {
            value = BitConverter.GetBytes(input.Ticks);
            return true;
        }

        protected virtual bool TryConvert(Guid input, IFormatProvider provider, out byte[] value)
        {
            value = input.ToByteArray();
            return true;
        }

        protected virtual bool TryConvert(DateTime input, IFormatProvider provider, out byte[] value)
        {
            value = BitConverter.GetBytes((input).ToBinary());
            return true;
        }

        protected virtual bool TryConvert(decimal input, IFormatProvider provider, out byte[] value)
        {
            var decBytes = new byte[16];
            GetBytes(input, decBytes);
            value = decBytes;
            return true;
        }

        protected virtual bool TryConvert(object input, IFormatProvider provider, out byte[] value)
        {
            byte[] bytes;
            if (input is Guid)
            {
                if (TryConvert((Guid)input, provider, out bytes))
                {
                    value = bytes;
                    return true;
                }

                value = null;
                return false;
            }

            if (input is DateTimeOffset)
            {
                if (TryConvert(((DateTimeOffset)input).DateTime, typeof(byte[]), provider, out var result))
                {
                    value = (byte[])result;
                    return true;
                }
            }

            if (input is TimeSpan)
            {
                if (TryConvert((TimeSpan)input, provider, out bytes))
                {
                    value = bytes;
                    return true;
                }

                value = null;
                return false;
            }

            value = null;
            return false;
        }

        protected virtual bool TryConvertEnum(object input, Type conversionType, IFormatProvider provider, out object value)
        {
            if (EnumTryParse(conversionType, Convert.ToString(input, provider), out value))
                return true;

            return false;
        }

        protected virtual bool TryConvert(string text, IFormatProvider provider, out byte[] value)
        {
            if (text == null)
            {
                value = null;
                return true;
            }

            try
            {
                value = Convert.FromBase64String(text);
                return true;
            }
            catch (Exception)
            {
            }

            var bytes = FromHexa(text);
            if (bytes != null)
            {
                value = bytes;
                return true;
            }

            value = null;
            return false;
        }

        protected virtual bool TryConvert(object input, IFormatProvider provider, out CultureInfo value)
        {
            if (input == null)
            {
                value = null;
                return true;
            }

            if (input is string name)
            {
                try
                {
                    value = new CultureInfo(name);
                    return true;
                }
                catch (CultureNotFoundException)
                {
                }

                if (int.TryParse(name, out var i))
                {
                    if (TryConvert(i, provider, out value))
                        return true;
                }
            }

            if (input is int lcid)
            {
                if (TryConvert(lcid, provider, out value))
                    return true;
            }

            value = null;
            return false;
        }

        protected virtual bool TryConvert(int lcid, IFormatProvider provider, out CultureInfo value)
        {
            try
            {
                value = new CultureInfo(lcid);
                return true;
            }
            catch (CultureNotFoundException)
            {
            }

            value = null;
            return false;
        }

        protected virtual bool TryConvert(object input, IFormatProvider provider, out DateTimeOffset value)
        {
            if (DateTimeOffset.TryParse(Convert.ToString(input, provider), provider, DateTimeStyles.None, out value))
                return true;

            if (TryConvert(input, provider, out DateTime dt))
            {
                value = new DateTimeOffset(dt);
                return true;
            }

            value = DateTimeOffset.MinValue;
            return false;
        }

        protected virtual bool TryConvert(object input, IFormatProvider provider, out TimeSpan value)
        {
            if (TimeSpan.TryParse(Convert.ToString(input, provider), provider, out value))
                return true;

            if (TryConvert(input, provider, out long l))
            {
                value = new TimeSpan(l);
                return true;
            }

            value = TimeSpan.Zero;
            return false;
        }

        protected virtual bool TryConvert(object input, IFormatProvider provider, out IntPtr value)
        {
            value = IntPtr.Zero;
            if (IsNullOrEmptyString(input))
                return false;

            if (IntPtr.Size == 4)
            {
                if (TryConvert(input, provider, out int i))
                {
                    value = new IntPtr(i);
                    return true;
                }
                return false;
            }

            if (TryConvert(input, provider, out long l))
            {
                value = new IntPtr(l);
                return true;
            }
            return false;
        }

        protected virtual bool TryConvert(object input, IFormatProvider provider, out Guid value)
        {
            if (IsNullOrEmptyString(input))
            {
                value = Guid.Empty;
                return false;
            }

            if (input is byte[] inputBytes)
            {
                if (inputBytes.Length != 16)
                {
                    value = Guid.Empty;
                    return false;
                }

                value = new Guid(inputBytes);
                return true;
            }

            return Guid.TryParse(Convert.ToString(input, provider), out value);
        }

        protected virtual bool TryConvert(object input, IFormatProvider provider, out ulong value)
        {
            if (IsNullOrEmptyString(input))
            {
                value = 0;
                return false;
            }

            if (!(input is string))
            {
                if (input is IConvertible ic)
                {
                    try
                    {
                        value = ic.ToUInt64(provider);
                        return true;
                    }
                    catch
                    {
                    }
                }
            }

            var styles = NumberStyles.Integer;
            var s = Convert.ToString(input, provider);
            if (NormalizeHexString(ref s))
            {
                styles |= NumberStyles.AllowHexSpecifier;
            }
            return ulong.TryParse(s, styles, provider, out value);
        }

        protected virtual bool TryConvert(object input, IFormatProvider provider, out ushort value)
        {
            if (IsNullOrEmptyString(input))
            {
                value = 0;
                return false;
            }

            if (!(input is string))
            {
                if (input is IConvertible ic)
                {
                    try
                    {
                        value = ic.ToUInt16(provider);
                        return true;
                    }
                    catch
                    {
                    }
                }
            }

            var styles = NumberStyles.Integer;
            var s = Convert.ToString(input, provider);
            if (NormalizeHexString(ref s))
            {
                styles |= NumberStyles.AllowHexSpecifier;
            }
            return ushort.TryParse(s, styles, provider, out value);
        }

        protected virtual bool TryConvert(object input, IFormatProvider provider, out decimal value)
        {
            if (IsNullOrEmptyString(input))
            {
                value = 0;
                return false;
            }

            if (!(input is string))
            {
                if (input is IConvertible ic)
                {
                    try
                    {
                        value = ic.ToDecimal(provider);
                        return true;
                    }
                    catch
                    {
                    }
                }
            }

            var styles = NumberStyles.Integer;
            var s = Convert.ToString(input, provider);
            if (NormalizeHexString(ref s))
            {
                styles |= NumberStyles.AllowHexSpecifier;
            }
            return decimal.TryParse(s, styles, provider, out value);
        }

        protected virtual bool TryConvert(object input, IFormatProvider provider, out float value)
        {
            if (IsNullOrEmptyString(input))
            {
                value = 0;
                return false;
            }

            if (!(input is string))
            {
                if (input is IConvertible ic)
                {
                    try
                    {
                        value = ic.ToSingle(provider);
                        return true;
                    }
                    catch
                    {
                    }
                }
            }

            var styles = NumberStyles.Integer;
            var s = Convert.ToString(input, provider);
            if (NormalizeHexString(ref s))
            {
                styles |= NumberStyles.AllowHexSpecifier;
            }
            return float.TryParse(s, styles, provider, out value);
        }

        protected virtual bool TryConvert(object input, IFormatProvider provider, out double value)
        {
            if (IsNullOrEmptyString(input))
            {
                value = 0;
                return false;
            }

            value = 0;

            if (!(input is string))
            {
                if (input is IConvertible ic)
                {
                    try
                    {
                        value = ic.ToDouble(provider);
                        return true;
                    }
                    catch
                    {
                    }
                }
            }

            var styles = NumberStyles.Integer;
            var s = Convert.ToString(input, provider);
            if (NormalizeHexString(ref s))
            {
                styles |= NumberStyles.AllowHexSpecifier;
            }
            return double.TryParse(s, styles, provider, out value);
        }

        protected virtual bool TryConvert(object input, IFormatProvider provider, out char value)
        {
            if (IsNullOrEmptyString(input))
            {
                value = '\0';
                return false;
            }

            if (!(input is string))
            {
                if (input is IConvertible ic)
                {
                    try
                    {
                        value = ic.ToChar(provider);
                        return true;
                    }
                    catch
                    {
                    }
                }
            }

            var s = Convert.ToString(input, provider);
            return char.TryParse(s, out value);
        }

        protected virtual bool TryConvert(object input, IFormatProvider provider, out DateTime value)
        {
            if (IsNullOrEmptyString(input))
            {
                value = DateTime.MinValue;
                return false;
            }

            if (!(input is string))
            {
                if (input is IConvertible ic)
                {
                    try
                    {
                        value = ic.ToDateTime(provider);
                        return true;
                    }
                    catch
                    {
                    }
                }
            }

            var s = Convert.ToString(input, provider);
            return DateTime.TryParse(s, provider, DateTimeStyles.None, out value);
        }

        protected virtual bool TryConvert(object input, IFormatProvider provider, out uint value)
        {
            if (IsNullOrEmptyString(input))
            {
                value = 0;
                return false;
            }

            if (!(input is string))
            {
                if (input is IConvertible ic)
                {
                    try
                    {
                        value = ic.ToUInt32(provider);
                        return true;
                    }
                    catch
                    {
                    }
                }
            }

            var styles = NumberStyles.Integer;
            var s = Convert.ToString(input, provider);
            if (NormalizeHexString(ref s))
            {
                styles |= NumberStyles.AllowHexSpecifier;
            }
            return uint.TryParse(s, styles, provider, out value);
        }

        protected virtual bool TryConvert(object input, IFormatProvider provider, out byte value)
        {
            if (IsNullOrEmptyString(input))
            {
                value = 0;
                return false;
            }

            if (!(input is string))
            {
                if (input is IConvertible ic)
                {
                    try
                    {
                        value = ic.ToByte(provider);
                        return true;
                    }
                    catch
                    {
                    }
                }
            }

            var styles = NumberStyles.Integer;
            var s = Convert.ToString(input, provider);
            if (NormalizeHexString(ref s))
            {
                styles |= NumberStyles.AllowHexSpecifier;
            }
            return byte.TryParse(s, styles, provider, out value);
        }

        protected virtual bool TryConvert(object input, IFormatProvider provider, out sbyte value)
        {
            if (IsNullOrEmptyString(input))
            {
                value = 0;
                return false;
            }

            if (!(input is string))
            {
                if (input is IConvertible ic)
                {
                    try
                    {
                        value = ic.ToSByte(provider);
                        return true;
                    }
                    catch
                    {
                    }
                }
            }

            var styles = NumberStyles.Integer;
            var s = Convert.ToString(input, provider);
            if (NormalizeHexString(ref s))
            {
                styles |= NumberStyles.AllowHexSpecifier;
            }
            return sbyte.TryParse(s, styles, provider, out value);
        }

        protected virtual bool TryConvert(object input, IFormatProvider provider, out short value)
        {
            if (IsNullOrEmptyString(input))
            {
                value = 0;
                return false;
            }

            value = 0;
            if (input is byte[] inputBytes)
            {
                if (inputBytes.Length == 2)
                {
                    value = BitConverter.ToInt16(inputBytes, 0);
                    return true;
                }
                return false;
            }

            if (!(input is string))
            {
                if (input is IConvertible ic)
                {
                    try
                    {
                        value = ic.ToInt16(provider);
                        return true;
                    }
                    catch
                    {
                    }
                }
            }

            var styles = NumberStyles.Integer;
            var s = Convert.ToString(input, provider);
            if (NormalizeHexString(ref s))
            {
                styles |= NumberStyles.AllowHexSpecifier;
            }
            return short.TryParse(s, styles, provider, out value);
        }

        protected virtual bool TryConvert(object input, IFormatProvider provider, out int value)
        {
            if (IsNullOrEmptyString(input))
            {
                value = 0;
                return false;
            }

            value = 0;
            if (input is byte[] inputBytes)
            {
                if (inputBytes.Length == 4)
                {
                    value = BitConverter.ToInt32(inputBytes, 0);
                    return true;
                }
                return false;
            }

            if (input is IntPtr)
            {
                value = ((IntPtr)input).ToInt32();
                return true;
            }

            if (!(input is string))
            {
                if (input is IConvertible ic)
                {
                    try
                    {
                        value = ic.ToInt32(provider);
                        return true;
                    }
                    catch
                    {
                    }
                }
            }

            var styles = NumberStyles.Integer;
            var s = Convert.ToString(input, provider);
            if (NormalizeHexString(ref s))
            {
                styles |= NumberStyles.AllowHexSpecifier;
            }
            return int.TryParse(s, styles, provider, out value);
        }

        protected virtual bool TryConvert(object input, IFormatProvider provider, out long value)
        {
            if (IsNullOrEmptyString(input))
            {
                value = 0;
                return false;
            }

            value = 0;
            if (input is byte[] inputBytes)
            {
                if (inputBytes.Length == 8)
                {
                    value = BitConverter.ToInt64(inputBytes, 0);
                    return true;
                }
                return false;
            }

            if (input is IntPtr)
            {
                value = ((IntPtr)input).ToInt64();
                return true;
            }

            if (!(input is string))
            {
                if (input is IConvertible ic)
                {
                    try
                    {
                        value = ic.ToInt64(provider);
                        return true;
                    }
                    catch
                    {
                    }
                }
            }

            var styles = NumberStyles.Integer;
            var s = Convert.ToString(input, provider);
            if (NormalizeHexString(ref s))
            {
                styles |= NumberStyles.AllowHexSpecifier;
            }
            return long.TryParse(s, styles, provider, out value);
        }

        protected virtual bool TryConvert(object input, IFormatProvider provider, out bool value)
        {
            value = false;
            if (input is byte[] inputBytes)
            {
                if (inputBytes.Length == 1)
                {
                    value = BitConverter.ToBoolean(inputBytes, 0);
                    return true;
                }
                return false;
            }

            if (TryConvert(input, typeof(long), provider, out var b))
            {
                value = (long)b != 0;
                return true;
            }

            var bools = Convert.ToString(input, provider);
            if (bools == null)
                return false; // arguable...

            bools = bools.Trim().ToLowerInvariant();
            if (bools == "y" || bools == "yes" || bools == "t" || bools.StartsWith("true"))
            {
                value = true;
                return true;
            }

            if (bools == "n" || bools == "no" || bools == "f" || bools.StartsWith("false"))
                return true;

            return false;
        }

        protected virtual bool TryConvert(object input, Type conversionType, IFormatProvider provider, out object value)
        {
            if (conversionType == null)
                throw new ArgumentNullException(nameof(conversionType));

            if (conversionType == typeof(object))
            {
                value = input;
                return true;
            }

            if (input == null || Convert.IsDBNull(input))
            {
                if (conversionType.IsNullableOfT())
                {
                    value = null;
                    return true;
                }

                if (conversionType.IsValueType)
                {
                    value = Activator.CreateInstance(conversionType);
                    return false;
                }

                value = null;
                return true;
            }

            var inputType = input.GetType();
            if (conversionType.IsAssignableFrom(inputType.GetTypeInfo()))
            {
                value = input;
                return true;
            }

            if (conversionType.IsNullableOfT())
            {
                // en empty string is successfully converted into a nullable
                var inps = input as string;
                if (string.IsNullOrWhiteSpace(inps))
                {
                    value = null;
                    return true;
                }

                var vtType = Nullable.GetUnderlyingType(conversionType);
                if (TryConvert(input, vtType, provider, out var vtValue))
                {
                    var nt = typeof(Nullable<>).MakeGenericType(vtType);
                    value = Activator.CreateInstance(nt, vtValue);
                    return true;
                }
                value = null;
                return false;
            }

            // enum must be before integers
            if (conversionType.IsEnum)
            {
                if (TryConvertEnum(input, conversionType, provider, out value))
                    return true;
            }

            var conversionCode = Type.GetTypeCode(conversionType);
            switch (conversionCode)
            {
                case TypeCode.Boolean:
                    bool boolValue;
                    if (TryConvert(input, provider, out boolValue))
                    {
                        value = boolValue;
                        return true;
                    }
                    break;

                case TypeCode.Byte:
                    byte byteValue;
                    if (TryConvert(input, provider, out byteValue))
                    {
                        value = byteValue;
                        return true;
                    }
                    break;

                case TypeCode.Char:
                    char charValue;
                    if (TryConvert(input, provider, out charValue))
                    {
                        value = charValue;
                        return true;
                    }
                    break;

                case TypeCode.DateTime:
                    DateTime dtValue;
                    if (TryConvert(input, provider, out dtValue))
                    {
                        value = dtValue;
                        return true;
                    }
                    break;

                case TypeCode.Decimal:
                    decimal decValue;
                    if (TryConvert(input, provider, out decValue))
                    {
                        value = decValue;
                        return true;
                    }
                    break;

                case TypeCode.Double:
                    double dblValue;
                    if (TryConvert(input, provider, out dblValue))
                    {
                        value = dblValue;
                        return true;
                    }
                    break;

                case TypeCode.Int16:
                    short i16Value;
                    if (TryConvert(input, provider, out i16Value))
                    {
                        value = i16Value;
                        return true;
                    }
                    break;

                case TypeCode.Int32:
                    int i32Value;
                    if (TryConvert(input, provider, out i32Value))
                    {
                        value = i32Value;
                        return true;
                    }
                    break;

                case TypeCode.Int64:
                    long i64Value;
                    if (TryConvert(input, provider, out i64Value))
                    {
                        value = i64Value;
                        return true;
                    }
                    break;

                case TypeCode.SByte:
                    sbyte sbyteValue;
                    if (TryConvert(input, provider, out sbyteValue))
                    {
                        value = sbyteValue;
                        return true;
                    }
                    break;

                case TypeCode.Single:
                    float fltValue;
                    if (TryConvert(input, provider, out fltValue))
                    {
                        value = fltValue;
                        return true;
                    }
                    break;

                case TypeCode.String:
                    if (input is byte[] inputBytes)
                    {
                        if (TryConvert(inputBytes, provider, out var str))
                        {
                            value = str;
                            return true;
                        }

                        value = null;
                        return false;
                    }

#if NETSTANDARD2_0
                    if (input is CultureInfo ci)
                    {
                        value = ci.Name;
                        return true;
                    }
#endif

                    var tc = TypeDescriptor.GetConverter(inputType);
                    if (tc != null && tc.CanConvertTo(typeof(string)))
                    {
                        value = (string)tc.ConvertTo(input, typeof(string));
                        return true;
                    }

                    value = Convert.ToString(input, provider);
                    return true;

                case TypeCode.UInt16:
                    ushort u16Value;
                    if (TryConvert(input, provider, out u16Value))
                    {
                        value = u16Value;
                        return true;
                    }
                    break;

                case TypeCode.UInt32:
                    uint u32Value;
                    if (TryConvert(input, provider, out u32Value))
                    {
                        value = u32Value;
                        return true;
                    }
                    break;

                case TypeCode.UInt64:
                    ulong u64Value;
                    if (TryConvert(input, provider, out u64Value))
                    {
                        value = u64Value;
                        return true;
                    }
                    break;

                case TypeCode.Object:
                    if (conversionType == typeof(Guid))
                    {
                        if (TryConvert(input, provider, out Guid gValue))
                        {
                            value = gValue;
                            return true;
                        }
                    }
                    else if (conversionType == typeof(IntPtr))
                    {
                        if (TryConvert(input, provider, out IntPtr ptr))
                        {
                            value = ptr;
                            return true;
                        }
                    }
                    else if (conversionType == typeof(CultureInfo))
                    {
                        if (TryConvert(input, provider, out CultureInfo cultureInfo))
                        {
                            value = cultureInfo;
                            return true;
                        }
                    }
                    else if (conversionType == typeof(Version))
                    {
                        if (Version.TryParse(Convert.ToString(input, provider), out Version version))
                        {
                            value = version;
                            return true;
                        }
                    }
                    else if (conversionType == typeof(DateTimeOffset))
                    {
                        if (TryConvert(input, provider, out DateTimeOffset dto))
                        {
                            value = dto;
                            return true;
                        }
                    }
                    else if (conversionType == typeof(TimeSpan))
                    {
                        if (TryConvert(input, provider, out TimeSpan ts))
                        {
                            value = ts;
                            return true;
                        }
                    }
                    else if (conversionType == typeof(byte[]))
                    {
                        byte[] bytes;
                        var inputCode = Type.GetTypeCode(inputType);
                        switch (inputCode)
                        {
                            case TypeCode.DBNull:
                                value = null;
                                return true;

                            case TypeCode.Boolean:
                                value = BitConverter.GetBytes((bool)input);
                                return true;

                            case TypeCode.Char:
                                value = BitConverter.GetBytes((char)input);
                                return true;

                            case TypeCode.Double:
                                value = BitConverter.GetBytes((double)input);
                                return true;

                            case TypeCode.Int16:
                                value = BitConverter.GetBytes((short)input);
                                return true;

                            case TypeCode.Int32:
                                value = BitConverter.GetBytes((int)input);
                                return true;

                            case TypeCode.Int64:
                                value = BitConverter.GetBytes((long)input);
                                return true;

                            case TypeCode.Single:
                                value = BitConverter.GetBytes((float)input);
                                return true;

                            case TypeCode.UInt16:
                                value = BitConverter.GetBytes((ushort)input);
                                return true;

                            case TypeCode.UInt32:
                                value = BitConverter.GetBytes((uint)input);
                                return true;

                            case TypeCode.UInt64:
                                value = BitConverter.GetBytes((ulong)input);
                                return true;

                            case TypeCode.Byte:
                                value = new[] { (byte)input };
                                return true;

                            case TypeCode.DateTime:
                                if (TryConvert((DateTime)input, provider, out bytes))
                                {
                                    value = bytes;
                                    return true;
                                }
                                value = null;
                                return false;

                            case TypeCode.Decimal:
                                if (TryConvert((decimal)input, provider, out bytes))
                                {
                                    value = bytes;
                                    return true;
                                }

                                value = null;
                                return false;

                            case TypeCode.SByte:
                                value = new[] { unchecked((byte)input) };
                                return true;

                            case TypeCode.String:
                                if (TryConvert((string)input, provider, out bytes))
                                {
                                    value = bytes;
                                    return true;
                                }

                                value = null;
                                return false;

                            default:
                                if (TryConvert(input, provider, out bytes))
                                {
                                    value = bytes;
                                    return true;
                                }
                                break;
                        }
                    }
                    break;
            }

            // catch many exceptions before they happen
            if (IsNumberType(conversionType) && IsNullOrEmptyString(input))
            {
                value = Activator.CreateInstance(conversionType);
                return false;
            }

            TypeConverter ctConverter = null;
            try
            {
                ctConverter = TypeDescriptor.GetConverter(conversionType);
                if (ctConverter != null && ctConverter.CanConvertFrom(inputType))
                {
                    if (provider is CultureInfo cultureInfo)
                    {
                        value = ctConverter.ConvertFrom(null, cultureInfo, input);
                    }
                    else
                    {
                        value = ctConverter.ConvertFrom(input);
                    }

                    return true;
                }
            }
            catch
            {
                // do nothing
            }

            try
            {
                var inputConverter = TypeDescriptor.GetConverter(inputType);
                if (inputConverter != null && inputConverter.CanConvertTo(conversionType))
                {
                    value = inputConverter.ConvertTo(null, provider as CultureInfo, input, conversionType);
                    return true;
                }
            }
            catch
            {
                // do nothing
            }

            var defaultValue = conversionType.IsValueType ? Activator.CreateInstance(conversionType) : null;
            try
            {
                if (ctConverter != null && !(input is string) && ctConverter.CanConvertFrom(typeof(string)))
                {
                    value = ctConverter.ConvertFrom(null, provider as CultureInfo, Convert.ToString(input, provider));
                    return true;
                }
            }
            catch
            {
                // do nothing
            }

            if (TryConvertUsingImplicitConverter(input, conversionType, provider, out value))
                return true;

            value = defaultValue;
            return false;
        }

        protected virtual bool TryConvertUsingImplicitConverter(object input, Type conversionType, IFormatProvider provider, out object value)
        {
            var op = ReflectionUtilities.GetImplicitConversion(input, conversionType);
            if (op == null)
            {
                value = default;
                return false;
            }

            value = op.Invoke(null, new object[] { input });
            return true;
        }
    }
}