using Sandbox;

[Hide]
public sealed class PlatformTest : Component
{
    protected override void OnFixedUpdate()
    {
        WorldPosition += Vector3.Right * (float)Math.Sin(Time.Now * 2f) * 400f * Time.Delta;
        //WorldPosition += Vector3.Up * (float)Math.Sin(Time.Now * 2f) * 200f * Time.Delta;
    }
}
