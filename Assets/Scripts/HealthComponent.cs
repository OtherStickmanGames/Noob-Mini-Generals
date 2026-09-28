using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

public class HealthComponent : MonoBehaviour
{
    [SerializeField] float maxHeath = 100;
    public float MaxValue => maxHeath;

    public Action<HealthComponent> valueChanged;

    public void Init(float maxHealth)
    {
        this.maxHeath = maxHealth;
        Value = maxHeath;
    }

    public float Value 
    {
        get => value; 
        set
        {
            this.value = Mathf.Clamp(value, 0, float.MaxValue);
            valueChanged?.Invoke(this);
        } 
    }

    [Space] [SerializeField]
    float value;

    private void Start()
    {
        Value = maxHeath;
    }


}
