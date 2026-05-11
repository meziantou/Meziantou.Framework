using Meziantou.Framework.Annotations;

[assembly: FastEnumAttribute(typeof(FastEnumGeneratorBenchmarks.SimpleEnum), ExtensionMethodNamespace = "FastEnumGeneratorBenchmarks.Generated")]
[assembly: FastEnumAttribute(typeof(FastEnumGeneratorBenchmarks.FlagsEnum), ExtensionMethodNamespace = "FastEnumGeneratorBenchmarks.Generated")]
[assembly: FastEnumAttribute(typeof(FastEnumGeneratorBenchmarks.SmallEnum), ExtensionMethodNamespace = "FastEnumGeneratorBenchmarks.Generated")]
[assembly: FastEnumAttribute(typeof(FastEnumGeneratorBenchmarks.MediumEnum), ExtensionMethodNamespace = "FastEnumGeneratorBenchmarks.Generated")]
[assembly: FastEnumAttribute(typeof(FastEnumGeneratorBenchmarks.LargeEnum), ExtensionMethodNamespace = "FastEnumGeneratorBenchmarks.Generated")]
[assembly: FastEnumAttribute(typeof(FastEnumGeneratorBenchmarks.MetadataFlagsEnum), ExtensionMethodNamespace = "FastEnumGeneratorBenchmarks.Generated")]
