using System.Text;

namespace Meziantou.Framework.Http
{
    public sealed class LinkHeaderValue
    {
        private static ReadOnlySpan<char> ParameterSeparators => new char[] { ' ', '\t', '=', ';', ',' };

        public string Url { get; }

        public string Rel => GetParameterValue("rel") ?? "";

        public string? GetParameterValue(string parameterName) => Parameters.FirstOrDefault(p => p.Key == parameterName).Value;

        public IReadOnlyList<KeyValuePair<string, string>> Parameters { get; }

        private LinkHeaderValue(string url!!, IReadOnlyList<KeyValuePair<string, string>> parameters)
        {
            Url = url;
            Parameters = parameters;
        }

        // https://httpwg.org/specs/rfc8288.html
        // https://datatracker.ietf.org/doc/html/rfc7230#section-3.2.3
        public static IEnumerable<LinkHeaderValue> Parse(string value) => Parse(value.AsSpan());

        public static IEnumerable<LinkHeaderValue> Parse(ReadOnlySpan<char> value)
        {
            var result = new List<LinkHeaderValue>();
            while (!value.IsEmpty)
            {
                value = ConsumeOptionalWhiteSpaces(value);
                if (value.IsEmpty)
                    break;

                if (value[0] is not '<')
                    break;

                // Remove the first '<'
                value = value[1..];
                var index = value.IndexOf('>');
                if (index == -1)
                    break;

                var targetLink = value[..index].ToString();
                value = value[(index + 1)..];

                // Parse parameters
                var parameters = new List<KeyValuePair<string, string>>();
                while (!value.IsEmpty && value[0] != ',')
                {
                    value = ConsumeOptionalWhiteSpaces(value);
                    if (value.IsEmpty)
                        break;

                    if (value[0] is not ';')
                        break;

                    value = value[1..];
                    value = ConsumeOptionalWhiteSpaces(value);
                    index = value.IndexOfAny(ParameterSeparators);

                    string parameterName;
                    var parameterValue = "";
                    if (index == -1)
                    {
                        parameterName = value.ToString();
                        value = ReadOnlySpan<char>.Empty;
                    }
                    else
                    {
                        parameterName = value[0..index].ToString();
                        value = value[index..];
                    }

                    value = ConsumeOptionalWhiteSpaces(value);
                    if (value.Length > 0 && value[0] is '=')
                    {
                        value = value[1..];
                        value = ConsumeOptionalWhiteSpaces(value);
                        if (value.Length > 0 && value[0] is '"')
                        {
                            value = value[1..];
                            var sb = new StringBuilder();
                            while (!value.IsEmpty)
                            {
                                var c = value[0];
                                if (c == '"')
                                {
                                    value = value[1..];
                                    break;
                                }
                                else if (c == '\\')
                                {
                                    if (value.Length > 0)
                                    {
                                        sb.Append(value[1]);
                                        value = value[2..];
                                    }
                                    else
                                    {
                                        value = value[1..];
                                        break;
                                    }
                                }
                                else
                                {
                                    sb.Append(c);
                                    value = value[1..];
                                }
                            }

                            parameterValue = sb.ToString();
                        }
                        else
                        {
                            index = value.IndexOfAny(';', ',');
                            if (index == -1)
                            {
                                parameterValue = value.ToString();
                                value = ReadOnlySpan<char>.Empty;
                            }
                            else
                            {
                                parameterValue = value[0..index].ToString();
                                value = value[index..];
                            }
                        }
                    }

#pragma warning disable CA1308 // Normalize strings to uppercase
                    parameters.Add(KeyValuePair.Create(parameterName.ToLowerInvariant(), parameterValue));
#pragma warning restore CA1308

                    value = ConsumeOptionalWhiteSpaces(value);
                }

                result.Add(new LinkHeaderValue(targetLink, parameters));

                if (value.Length > 0 && value[0] is ',')
                {
                    value = value[1..];
                }
            }

            return result;

            static ReadOnlySpan<char> ConsumeOptionalWhiteSpaces(ReadOnlySpan<char> value)
            {
                for (var i = 0; i < value.Length; i++)
                {
                    if (value[i] is not ' ' and not '\t')
                        return value[i..];
                }

                return ReadOnlySpan<char>.Empty;
            }
        }
    }
}
