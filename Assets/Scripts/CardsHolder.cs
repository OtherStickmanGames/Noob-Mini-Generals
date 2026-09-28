using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CardsHolder : MonoBehaviour
{
    public List<CardData> cardDatas;

    public CardData GetCardData(CardType cardType)
    {
        return cardDatas.Find(d => d.cardType == cardType);
    }
}

[System.Serializable]
public class Card
{
    public string name;
    public CardType cardType;
    public List<float> values;
    public int count;
    public int lvl;
    public int price;
}

[System.Serializable]
public class CardData
{
    public string name;
    public CardType cardType;
    public List<float> values;
    public int price;

    public Card ToCard()
    {
        return new Card()
        {
            name = name,
            cardType = cardType,
            values = values,
            price = price
        };
    }

    public Card ToCardForAI()
    {
        var lvl = Random.Range(1, 3);

        return new Card()
        {
            name = name,
            cardType = cardType,
            values = CalculateValues(lvl),
            price = price,
            lvl = lvl,
        };
    }

    public List<float> CalculateValues(int lvl)
    {
        List<float> values = this.values;

        if(cardType == CardType.TurrelsAttackDistance)
        {
            values[0] *= lvl;
        }

        if(cardType == CardType.AllFareRate)
        {
            values[0] *= lvl;
        }

        if (cardType == CardType.TroopersIncreaseHealth)
        {
            values[0] *= lvl;
        }

        if (cardType == CardType.MainBuildIncreaseHealth)
        {
            values[0] *= lvl;
        }

        return values;
    }
}


public enum CardType
{
    AllFareRate = 0,
    GoldExtractionRate = 1,
    TroopersIncreaseHealth = 2,
    TurrelsAttackDistance = 3,
    MainBuildIncreaseHealth = 4,
}
