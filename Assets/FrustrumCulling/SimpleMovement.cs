using UnityEngine;

public class SimpleMovement : MonoBehaviour
{
    public float speed = 5.0f;         // Speed of movement
    public float rotationSpeed = 100.0f; // Speed of rotation

    void Update()
    {
        // Movement - using transform.Translate for simplicity
        float translation = Input.GetAxis("Vertical") * speed * Time.deltaTime;
        float strafe = Input.GetAxis("Horizontal") * speed * Time.deltaTime;

        // Apply movement in the local space of the GameObject
        transform.Translate(strafe, 0, translation, Space.Self);

        // Rotation
        float rotation = Input.GetAxis("Rotation") * rotationSpeed * Time.deltaTime;

        // Apply rotation around the local y-axis of the GameObject
        transform.Rotate(0, rotation, 0, Space.Self);
    }
}
