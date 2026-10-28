using System;

namespace FossPDF.UnitTests.Parity
{
    /// <summary>
    /// Marks a static method as the executable implementation of a parity fixture.
    /// The fixture is discovered by reflection so that activating a fixture only requires
    /// adding the method and updating the catalog, not editing a shared registry.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    internal sealed class ParityFixtureRunnerAttribute : Attribute
    {
        public ParityFixtureRunnerAttribute(string fixtureId)
        {
            FixtureId = fixtureId;
        }

        public string FixtureId { get; }
    }
}
