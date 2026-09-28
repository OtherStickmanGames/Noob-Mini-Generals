using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RotaTeso : MonoBehaviour
{
    [SerializeField] Transform target;

    private void Update()
    {
        var direction = target.transform.position - transform.position;
        var rotation = Quaternion.LookRotation(direction);
        transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.LookRotation(direction), Time.deltaTime);

    }
}
