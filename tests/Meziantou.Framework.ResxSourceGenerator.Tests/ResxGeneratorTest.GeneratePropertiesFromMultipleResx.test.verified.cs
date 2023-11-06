
// Debug info:
// key: {TempPath}test
// files: {TempPath}test.resx, {TempPath}test.en.resx, {TempPath}test.fr-FR.resx
// RootNamespace (metadata): Test
// ProjectDir (metadata): {TempPath}
// Namespace / DefaultResourcesNamespace (metadata): 
// ResourceName (metadata): 
// ClassName (metadata): 
// AssemblyName: compilation
// RootNamespace (computed): Test
// ProjectDir (computed): {TempPath}
// defaultNamespace: Test
// defaultResourceName: Test.test
// Namespace: Test
// ResourceName: Test.test
// ClassName: test

#nullable enable
namespace Test
{
    internal partial class test
    {
        private static global::System.Resources.ResourceManager? resourceMan;

        public test() { }

        /// <summary>
        ///   Returns the cached ResourceManager instance used by this class.
        /// </summary>
        [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        public static global::System.Resources.ResourceManager ResourceManager
        {
            get
            {
                if (resourceMan is null) 
                {
                    resourceMan = new global::System.Resources.ResourceManager("Test.test", typeof(test).Assembly);
                }

                return resourceMan;
            }
        }

        /// <summary>
        ///   Overrides the current thread's CurrentUICulture property for all
        ///   resource lookups using this strongly typed resource class.
        /// </summary>
        [System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Advanced)]
        public static global::System.Globalization.CultureInfo? Culture { get; set; }

        [return: global::System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute("defaultValue")]

        public static object? GetObject(global::System.Globalization.CultureInfo? culture, string name, object? defaultValue)
        {
            culture ??= Culture;
            object? obj = ResourceManager.GetObject(name, culture);
            if (obj == null)
            {
                return defaultValue;
            }

            return obj;
        }
        
        public static object? GetObject(global::System.Globalization.CultureInfo? culture, string name)
        {
            return GetObject(culture: culture, name: name, defaultValue: null);
        }

        public static object? GetObject(string name)
        {
            return GetObject(culture: null, name: name, defaultValue: null);
        }

        [return: global::System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute("defaultValue")]

        public static object? GetObject(string name, object? defaultValue)
        {
            return GetObject(culture: null, name: name, defaultValue: defaultValue);
        }

        public static global::System.IO.Stream? GetStream(string name)
        {
            return GetStream(culture: null, name: name);
        }

        public static global::System.IO.Stream? GetStream(global::System.Globalization.CultureInfo? culture, string name)
        {
            culture ??= Culture;
            return ResourceManager.GetStream(name, culture);
        }

        public static string? GetString(global::System.Globalization.CultureInfo? culture, string name)
        {
            return GetString(culture: culture, name: name, args: null);
        }

        public static string? GetString(global::System.Globalization.CultureInfo? culture, string name, params object?[]? args)
        {
            culture ??= Culture;
            string? str = ResourceManager.GetString(name, culture);
            if (str == null)
            {
                return null;
            }

            if (args != null)
            {
                return string.Format(culture, str, args);
            }
            else
            {
                return str;
            }
        }
        
        public static string? GetString(string name, params object?[]? args)
        {
            return GetString(culture: null, name: name, args: args);
        }

        [return: global::System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute("defaultValue")]
        
        public static string? GetString(string name, string? defaultValue)
        {
            return GetStringWithDefault(culture: null, name: name, defaultValue: defaultValue, args: null);
        }

        public static string? GetString(string name)
        {
            return GetStringWithDefault(culture: null, name: name, defaultValue: null, args: null);
        }
        
        [return: global::System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute("defaultValue")]

        public static string? GetStringWithDefault(global::System.Globalization.CultureInfo? culture, string name, string? defaultValue)
        {
            return GetStringWithDefault(culture: culture, name: name, defaultValue: defaultValue, args: null);
        }

        [return: global::System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute("defaultValue")]

        public static string? GetStringWithDefault(global::System.Globalization.CultureInfo? culture, string name, string? defaultValue, params object?[]? args)
        {
            culture ??= Culture;
            string? str = ResourceManager.GetString(name, culture);
            if (str == null)
            {
                if (defaultValue == null || args == null)
                {
                    return defaultValue;
                }
                else
                {
                    return string.Format(culture, defaultValue, args);
                }
            }

            if (args != null)
            {
                return string.Format(culture, str, args);
            }
            else
            {
                return str;
            }
        }

        [return: global::System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute("defaultValue")]

        public static string? GetStringWithDefault(string name, string? defaultValue, params object?[]? args)
        {
            return GetStringWithDefault(culture: null, name: name, defaultValue: defaultValue, args: args);
        }

        [return: global::System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute("defaultValue")]

        public static string? GetStringWithDefault(string name, string? defaultValue)
        {
            return GetStringWithDefault(culture: null, name: name, defaultValue: defaultValue, args: null);
        }


        /// <summary>
       ///   <para>Looks up a localized string for "AAA".</para>
       ///   <para>Value: "Value".</para>
       /// </summary>
        public static string? @AAA
        {
            get
            {
                return GetString("AAA");
            }
        }


        /// <summary>
       ///   <para>Looks up a localized string for "HelloWorld".</para>
       ///   <para>Value: "Hello {0}!".</para>
       /// </summary>
        public static string? @HelloWorld
        {
            get
            {
                return GetString("HelloWorld");
            }
        }


        /// <summary>
       ///   <para>Looks up a localized string for "HelloWorld".</para>
       ///   <para>Value: "Hello {0}!".</para>
       /// </summary>
        public static string? FormatHelloWorld(global::System.Globalization.CultureInfo? provider, object? arg0)
        {
            return GetString(provider, "HelloWorld", arg0);
        }


        /// <summary>
       ///   <para>Looks up a localized string for "HelloWorld".</para>
       ///   <para>Value: "Hello {0}!".</para>
       /// </summary>
        public static string? FormatHelloWorld(object? arg0)
        {
            return GetString("HelloWorld", arg0);
        }


        /// <summary>
       ///   <para>Looks up a localized string for "HelloWorld2".</para>
       ///   <para>Value: "Hello {0}!".</para>
       /// </summary>
        public static string? @HelloWorld2
        {
            get
            {
                return GetString("HelloWorld2");
            }
        }


        /// <summary>
       ///   <para>Looks up a localized string for "HelloWorld2".</para>
       ///   <para>Value: "Hello {0}!".</para>
       /// </summary>
        public static string? FormatHelloWorld2(global::System.Globalization.CultureInfo? provider, object? arg0)
        {
            return GetString(provider, "HelloWorld2", arg0);
        }


        /// <summary>
       ///   <para>Looks up a localized string for "HelloWorld2".</para>
       ///   <para>Value: "Hello {0}!".</para>
       /// </summary>
        public static string? FormatHelloWorld2(object? arg0)
        {
            return GetString("HelloWorld2", arg0);
        }


        /// <summary>
       ///   <para>Looks up a localized string for "Sample".</para>
       ///   <para>Value: "Value".</para>
       /// </summary>
        public static string? @Sample
        {
            get
            {
                return GetString("Sample");
            }
        }

    }

    internal partial class testNames
    {
        public const string @Sample = "Sample";
        public const string @HelloWorld2 = "HelloWorld2";
        public const string @AAA = "AAA";
        public const string @HelloWorld = "HelloWorld";
    }
}
