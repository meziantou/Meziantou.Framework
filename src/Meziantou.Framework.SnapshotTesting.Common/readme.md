# Meziantou.Framework.SnapshotTesting.Common

`Meziantou.Framework.SnapshotTesting.Common` exposes environment detectors for CI/build servers and continuous test runners.

````c#
if (BuildServerDetector.Detected || ContinuousTestingDetector.Detected)
{
    // Running in CI/build server or continuous testing
}
````
