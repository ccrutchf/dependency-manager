using Xunit;

namespace DependencyManager.Tests;

// PathLookup.Probe is a shared mutable static. Tests that read the default probe
// (PathLookupTests) must not run in parallel with tests that swap it out
// (PlannerTests' requires coverage). xUnit serializes classes in the same collection.
[CollectionDefinition("PathLookup")]
public sealed class PathLookupCollection;
