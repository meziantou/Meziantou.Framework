// semver: parsing, comparing, sorting and incrementing versions, and the ranges of npm
import semver from "semver";

const versions = ["1.2.3", "v1.2.4-beta.1", "1.2.4-beta.10", "1.2.4-beta.2", "1.2.4", "2.0.0-rc.1+build.5", "0.0.1", "10.0.0", "1.10.0", "=1.0.0", "invalid", "1.2"];
const ranges = ["^1.2.3", "~1.2.3", ">=1.2.0 <2.0.0", "1.x || >=2.5.0", "1.2.3 - 2.3.4", "*", "^0.0.1", ">1.2.4-alpha", "<=1.2.3 || ^10"];

export default () => {
  const valid = versions.filter(version => semver.valid(semver.clean(version) ?? version) !== null);
  return JSON.stringify({
    parsed: versions.map(version => [version, semver.valid(version), semver.clean(version), semver.coerce(version)?.version ?? null]),
    sorted: semver.sort(valid.map(version => semver.clean(version))),
    descending: semver.rsort(valid.map(version => semver.clean(version))),
    satisfies: ranges.map(range => [range, semver.validRange(range), valid.filter(version => semver.satisfies(version, range, { includePrerelease: range.includes("alpha") }))]),
    maxSatisfying: ranges.map(range => semver.maxSatisfying(valid, range)),
    minVersion: ranges.map(range => semver.minVersion(range)?.version),
    increments: ["major", "minor", "patch", "premajor", "preminor", "prepatch", "prerelease"].map(type => semver.inc("1.2.3-alpha.1", type, "beta")),
    diff: [["1.2.3", "2.0.0"], ["1.2.3", "1.2.4-pre"], ["1.2.3", "1.2.3"]].map(([a, b]) => semver.diff(a, b)),
    compare: [semver.gt("1.10.0", "1.9.9"), semver.lt("1.0.0-alpha", "1.0.0"), semver.eq("1.0.0", "v1.0.0"), semver.compareBuild("1.0.0+1", "1.0.0+2")],
    intersects: semver.intersects("^1.2.0", ">=1.5.0 <3"),
    subset: semver.subset("^1.2.3", "^1.0.0"),
    simplified: semver.simplifyRange(["1.1.0", "1.2.0", "1.2.1", "1.3.0", "2.0.0"], "1.1.0 || 1.2.0 || 1.2.1 || 2.0.0"),
    prerelease: semver.prerelease("1.2.3-alpha.1.beta"),
  }, null, 1);
};
