using Meziantou.Framework.MediaTags.Samples;

// The readme examples are compiled in CI to keep the documentation in sync with the API. They modify song.mp3
// from the current directory, so the tests do not run them.
ReadmeExamples.UpdateTags();
ReadmeExamples.ReplaceTags();
