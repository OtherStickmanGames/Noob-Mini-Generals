using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Inst;

    public bool printNeuroLog;

    private void Awake()
    {
        Inst = this;
    }
}
