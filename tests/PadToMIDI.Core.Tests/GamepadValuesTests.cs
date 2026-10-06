using PadToMIDI.Core.Input;
using Xunit;

namespace PadToMIDI.Core.Tests;

public sealed class GamepadValuesTests
{
    [Theory]
    [InlineData(PhysicalControl.LeftTrigger, -1, 0)]
    [InlineData(PhysicalControl.RightTrigger, 2, 1)]
    [InlineData(PhysicalControl.LeftStickX, -2, -1)]
    [InlineData(PhysicalControl.LeftStickY, 2, 1)]
    [InlineData(PhysicalControl.RightStickX, 0, 0)]
    [InlineData(PhysicalControl.RightStickY, 0.25f, 0.25f)]
    public void AnalogValuesStayWithinContract(PhysicalControl control, float input, float expected)
    {
        var values = new GamepadValues().WithValue(control, input);
        Assert.Equal(expected, values.GetValue(control));
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void RejectsNonFiniteInput(float value) => Assert.Throws<ArgumentOutOfRangeException>(
        () => new GamepadValues().WithValue(PhysicalControl.LeftTrigger, value));

    [Fact]
    public void EveryNormalizedControlIsIndependent()
    {
        foreach (var control in Enum.GetValues<PhysicalControl>())
        {
            var values = new GamepadValues().WithValue(control, 1);
            foreach (var other in Enum.GetValues<PhysicalControl>())
                Assert.Equal(other == control ? 1 : 0, values.GetValue(other));
        }
    }
}
