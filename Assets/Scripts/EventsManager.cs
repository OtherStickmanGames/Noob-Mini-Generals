using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using UnityEngine.Events;

public class EventsHolder
{

    public class PlayerSpawned : UnityEvent<Player> { }

    public static PlayerSpawned playerSpawnedMine = new PlayerSpawned();

    //-----------------------------------------------------------------------

    public class StickmanDestroyed : UnityEvent<Player> { }

    public static StickmanDestroyed onStickmanDestroyed = new();

    //-----------------------------------------------------------------------

    public class Victory : UnityEvent<Team> { }

    public static Victory onVictory = new();

    //-----------------------------------------------------------------------
    public class BlockConnected : UnityEvent<GameObject> { }

    public static BlockConnected onBlockConnected = new();

    //-----------------------------------------------------------------------

    public class MissionComplete : UnityEvent { }

    public static MissionComplete onMissionComplete = new();

    //-----------------------------------------------------------------------

    public class MissionDefeat : UnityEvent { }

    public static MissionDefeat onMissionDefeat = new();

    //-----------------------------------------------------------------------


    public class NeuronTeached : UnityEvent { }

    public static NeuronTeached neuronTeached = new();

    //-----------------------------------------------------------------------

    public class PlayerSpawnedAny : UnityEvent<Player> { }

    public static PlayerSpawnedAny playerSpawnedAny = new();

    //-----------------------------------------------------------------------

    public class LeftJoystickMoved : UnityEvent<Vector2> { }

    public static LeftJoystickMoved leftJoystickMoved = new LeftJoystickMoved();

    //-----------------------------------------------------------------------

    public class LeftJoystickUp : UnityEvent { }

    public static LeftJoystickUp onLeftJoystickUp = new();

    //-----------------------------------------------------------------------


    public class RightJoystickMoved : UnityEvent<Vector2> { }

    public static RightJoystickMoved rightJoystickMoved = new RightJoystickMoved();

    //-----------------------------------------------------------------------

    public class RightJoystickUp : UnityEvent { }

    public static RightJoystickUp rightJoystickUp = new RightJoystickUp();

    //-----------------------------------------------------------------------

    public class JumpClicked : UnityEvent { }

    public static JumpClicked jumpClicked = new JumpClicked();

    //-----------------------------------------------------------------------

    public class PunchClicked : UnityEvent { }

    public static PunchClicked onPunchClicked = new();

    //-----------------------------------------------------------------------


    //public class WeaponPicked : UnityEvent<Player, Weapon> { }

    //public static WeaponPicked weaponPicked = new();

    //-----------------------------------------------------------------------

    //public class WeaponThrowed : UnityEvent<Player, Weapon> { }

    //public static WeaponThrowed weaponThrowed = new WeaponThrowed();

    //-----------------------------------------------------------------------

    //public class WeaponTriggered : UnityEvent<Player, Weapon> { }

    //public static WeaponTriggered weaponTriggered = new WeaponTriggered();

    //-----------------------------------------------------------------------

    //public class WeaponTriggerExit : UnityEvent<Player, Weapon> { }

    //public static WeaponTriggerExit weaponTriggerExit = new WeaponTriggerExit();

    //-----------------------------------------------------------------------

    public class PlayerKeepItem : UnityEvent<GameObject> { }

    public static PlayerKeepItem onPlayerKeepItem = new();

    //-----------------------------------------------------------------------

    public class PlayerDropItem : UnityEvent { }

    public static PlayerDropItem onPlayerDropItem = new();

    //-----------------------------------------------------------------------

    //public class MeleeStucked : UnityEvent<Melee> { }

    //public static MeleeStucked onMeleeStucked = new();

    //-----------------------------------------------------------------------

    //public class WeaponSetInited : UnityEvent<WeaponSet> { }

    //public static WeaponSetInited weaponSetInited = new WeaponSetInited();

    //-----------------------------------------------------------------------
    public class ObjectSpawned : UnityEvent<GameObject> { }

    public static ObjectSpawned onObjectSpawned = new();

    //-----------------------------------------------------------------------

    public class CritPunch : UnityEvent<Player> { }

    public static CritPunch onCritPunch = new();

    //-----------------------------------------------------------------------

    public class KeySpaceDown : UnityEvent { }

    public static KeySpaceDown onKeySpaceDown = new();

    //-----------------------------------------------------------------------

    //public class BlockMined : UnityEvent<Mineable> { }

    //public static BlockMined blockMined = new();

    //-----------------------------------------------------------------------

    public class BlockBuilded : UnityEvent<Vector2, int> { }

    public static BlockBuilded blockBuilded = new();

    //-----------------------------------------------------------------------

    public class BlockMineAnimation : UnityEvent<Vector2, int> { }

    public static BlockMineAnimation blockMineAnimation = new();

    //-----------------------------------------------------------------------
    public class BlockMineEffect : UnityEvent<Vector2, int> { }

    public static BlockMineEffect blockMineEffect = new();

    //-----------------------------------------------------------------------

    //========================= UI ============================

    //public class CardWeaponUpdate : UnityEvent<User.Weapon> { }

    //public static CardWeaponUpdate onCardWeaponUpdate = new();

    //-----------------------------------------------------------------------

    public class BtnCamClicked : UnityEvent { }

    public static BtnCamClicked onBtnCamClicked = new();

    //-----------------------------------------------------------------------

    public class InventoryClick : UnityEvent<Player> { }

    public static InventoryClick inventoryClick = new();

    //-----------------------------------------------------------------------

    //public class ItemTaked : UnityEvent<Player, Item> { }

    //public static ItemTaked itemTakedToQuick = new(); 
    //public static ItemTaked itemTakedToMain = new();

    //-----------------------------------------------------------------------

    //public class ItemUpdate : UnityEvent<Player, Item> { }

    //public static ItemUpdate itemUpdate = new();

    //-----------------------------------------------------------------------

}
