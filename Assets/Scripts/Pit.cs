using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Pit : MonoBehaviour
{
    [SerializeField] Building building;
    [SerializeField] PitType pitType;

    public PitType PitType => pitType;

}

public enum PitType
{
    Gold,
    Iron
}
