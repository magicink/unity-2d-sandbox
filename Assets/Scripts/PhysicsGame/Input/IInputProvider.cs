using System;
using UnityEngine;

public interface IInputProvider
{
    event Action<Vector2, int> Begin; // screen position, touch id
    event Action<Vector2, int> Move;
    event Action<Vector2, int> End;

    void Enable();
    void Disable();
    }
