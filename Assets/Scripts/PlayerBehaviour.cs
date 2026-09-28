using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerBehaviour : MonoBehaviour
{
    [SerializeField] LayerMask lm;
    [SerializeField] int distance = 300;
    Player player;

    private void Start()
    {
        player = GetComponent<Player>();
    }

    private void Update()
    {
        CheckMouseClick();
        KeyboardCheck();
    }

    void CheckMouseClick()
    {
        if (!Input.GetMouseButtonDown(0))
            return;

        if (Physics.Raycast(Camera.main.ScreenPointToRay(Input.mousePosition), out RaycastHit hit, distance))
        {
            //print(hit.collider);

            //FindObjectOfType<Unit>().SetDestination(hit.point);
        }


        //if (Physics.Raycast(Camera.main.transform.position, Camera.main.transform.forward, out RaycastHit hit, distance, lm))
        //{
        //    print(hit.collider);
        //}

    }

    void KeyboardCheck()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            player.SpawnUnit(UnitType.Trooper, TrooperType.LightTank);
        }
    }
}
