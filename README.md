# Shrimple Character Controller

https://github.com/user-attachments/assets/374eea5b-7106-4c3e-8cb6-9bb56a1ff511

A shrimple yet versatile Character Controller/Move Helper that performs great.
Performs 60%-200% better than standard Character Controller.


You can find example scenes for the Walker, Roller, and Flyer here: [Controller Example Scenes](https://github.com/Small-Fish-Dev/shrimple_character_controller/tree/main/Assets/scenes)

Made from scratch using the classic [Collide and Slide](https://www.peroxide.dk/papers/collision/collision.pdf) method.

For any issues please report them here: [Github Issues](https://github.com/Small-Fish-Dev/shrimple_character_controller/issues)

# How to use
You can find code examples for the Walker, Roller, and Flyer here: [Example Controllers](https://github.com/Small-Fish-Dev/shrimple_character_controller/tree/main/code/Examples)

It all boils down to:

```csharp
Controller.WishVelocity = Vector3.Forward;
Controller.Move();
```

You'll have to call `.Move()` only if your Controller options "Manual Update" is set to true. Otherwise you can set it to false and it will always be simulated.

You are also able to manually update the GameObject's transform rather than letting the Controller do it:
```csharp
Controller.WishVelocity = wishDirection * wishSpeed;
var controllerResult = Controller.Move( false );

if ( controllerResult.Position.z <= 999f ) // Out of bounds!
{
    Destroy();
}
else
{
    Transform.Position = controllerResult.Position;
    MyVelocity = controllerResult.Velocity;
}
```
