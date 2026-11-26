using System.Text.Json;
using Mizon.Windows.Services.Gamepad;

internal static class GamepadParityTests
{
    internal static void Run(Action<bool, string> check)
    {
        using var source = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "auth-gamepad-v1.json"));
        using var fixture = JsonDocument.Parse(source);
        var reference = fixture.RootElement;
        check(reference.GetProperty("version").GetInt32() == 1, "Shared gamepad fixture version");
        check(reference.GetProperty("directionInitialRepeatMs").GetInt32() == AuthGamepadInputReducer.InitialRepeatMilliseconds
            && reference.GetProperty("directionRepeatMs").GetInt32() == AuthGamepadInputReducer.RepeatMilliseconds,
            "Shared direction timing");

        foreach (var scenario in reference.GetProperty("inputCases").EnumerateArray())
        {
            var reducer = new AuthGamepadInputReducer();
            var name = scenario.GetProperty("name").GetString();
            foreach (var frame in scenario.GetProperty("frames").EnumerateArray())
            {
                if (frame.TryGetProperty("reset", out var reset) && reset.GetBoolean()) reducer.Reset();
                var buttons = AuthGamepadButtons.None;
                if (frame.TryGetProperty("buttons", out var pressed))
                    foreach (var button in pressed.EnumerateArray())
                        buttons |= Enum.Parse<AuthGamepadButtons>(button.GetString()!);
                var reading = new AuthGamepadReading(buttons, Axis(frame, "leftX"), Axis(frame, "leftY"),
                    Axis(frame, "rightY"), Axis(frame, "leftTrigger"), Axis(frame, "rightTrigger"));
                var time = frame.GetProperty("ms").GetInt64();
                var actual = reducer.TryRead(reading, time, out var action) ? action.ToString() : null;
                check(actual == frame.GetProperty("action").GetString(), $"Shared gamepad scenario {name} at {time} ms");
            }
        }

        foreach (var feedback in reference.GetProperty("feedback").EnumerateArray())
        {
            var kind = Enum.Parse<AuthGamepadFeedback>(feedback.GetProperty("kind").GetString()!);
            check(AuthGamepadFeedbackPattern.Duration(kind) == feedback.GetProperty("durationMs").GetInt32(),
                $"Shared {kind} haptic duration");
            foreach (var sample in feedback.GetProperty("samples").EnumerateArray())
            {
                var time = sample[0].GetInt64();
                var result = AuthGamepadFeedbackPattern.Sample(kind, time);
                check(Math.Abs(result.LeftMotor - sample[1].GetDouble()) < 0.000001
                    && Math.Abs(result.RightMotor - sample[2].GetDouble()) < 0.000001
                    && result.IsComplete == sample[3].GetBoolean(), $"Shared {kind} haptic sample at {time} ms");
            }
        }
    }

    private static double Axis(JsonElement frame, string name) =>
        frame.TryGetProperty(name, out var axis) ? axis.GetDouble() : 0;
}
