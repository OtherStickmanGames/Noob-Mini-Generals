using System.Collections;
using System.Collections.Generic;
using UnityEngine.UI;
using UnityEngine;
using System.Linq;
using TMPro;

public class UI : MonoBehaviour
{
    [SerializeField] TMP_Text txtPlayer_1;
    [SerializeField] TMP_Text txtPlayer_2;

    Player player_1;
    Player player_2;
    NeuronNetwork neuron_1;
    AIBehaviour aIBehaviour_1;
    NeuronNetwork neuron_2;
    AIBehaviour aIBehaviour_2;

    float updateTimer;

    private void Start()
    {
        var players = FindObjectsOfType<Player>().ToList();
        player_1 = players.Find(p => p.Team == Team.One);
        player_2 = players.Find(p => p.Team == Team.Two);
        neuron_1 = player_1.GetComponent<NeuronNetwork>();
        aIBehaviour_1 = player_1.GetComponent<AIBehaviour>();
        neuron_2 = player_2.GetComponent<NeuronNetwork>();
        aIBehaviour_2 = player_2.GetComponent<AIBehaviour>();
    }

    private void Update()
    {
        updateTimer += Time.deltaTime;

        if (updateTimer > 1)
        {
            updateTimer = 0;
            txtPlayer_1.text = $"Golda: {player_1.CountGold}\nIronchick:{player_1.CountIron}\nИтерация обучения {neuron_1.LearnIteration}\nКоличество попыток на наборе {aIBehaviour_1.CountTryedOnSet}" +
                $"\nШаг А = {neuron_1.aStep}\nПорог весов = {neuron_1.curWeightRange}" +
                $"\nКоличество NaN ошибок {neuron_1.CountNaNError}" +
                $"\nОшибка сети = {neuron_1.curErr}" +
                $"\nМин. ошибка = {neuron_1.minErr}";

            txtPlayer_2.text = $"Golda: {player_2.CountGold}\nIronchick:{player_2.CountIron}\nИтерация обучения {neuron_2.LearnIteration}\nКоличество попыток на наборе {aIBehaviour_2.CountTryedOnSet}" +
                $"\nШаг А = {neuron_2.aStep}\nПорог весов = {neuron_2.curWeightRange}" +
                $"\nКоличество NaN ошибок {neuron_2.CountNaNError}" +
                $"\nОшибка сети = {neuron_2.curErr}" +
                $"\nМин. ошибка = {neuron_2.minErr}";
        }
    }
}
