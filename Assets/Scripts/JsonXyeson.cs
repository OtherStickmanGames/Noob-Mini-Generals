using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System.IO;

public class JsonXyeson : MonoBehaviour
{

    static string dataPath
    {
#if UNITY_ANDROID
        get => Application.persistentDataPath;
#else
        get => Application.dataPath;
#endif
    }

    public static void SaveFile(Team team, List<InputData> datas)
    {
        var ebala = new HolderJoson { data = datas };
        var json = JsonUtility.ToJson(ebala);
        File.WriteAllText($"{dataPath}/ActionSet{team}.json", json);
    }

    public static void SaveNeuronState(Team team, NeuronState neuronState)
    {
        var json = Json.Serialize(neuronState);
        //var json = JsonUtility.ToJson(neuronState);
        File.WriteAllText($"{dataPath}/State{team}.json", json);
    }

    public static NeuronState LoadNeuronState(Team team)
    {
        var path = $"{dataPath}/State{team}.json";
        if (File.Exists(path))
        {
            var file = File.ReadAllText(path);
            return Json.Deserialize<NeuronState>(file);
        }
        else
        {
            return null;
        }
    }

    public static List<InputData> LoadData(Team team)
    {
        var path = $"{dataPath}/ActionSet{team}.json";
        if (File.Exists(path))
        {
            var file = File.ReadAllText(path);
            return JsonUtility.FromJson<HolderJoson>(file).data;
        }
        else
        {
            if(team == Team.Two)
            {
                List<InputData> res = new()
                {
                    new InputData()
                    {
                        inputs = new List<float>() { 3, 22, 1, 0, 10, 0, 0, 0 },
                        actionIndex = 5,
                    },
                    new InputData()
                    {
                        inputs = new List<float>() { 6, 28, 1, 0, 10, 0, 0, 0 },
                        actionIndex = 0,
                    },
                    new InputData()
                    {
                        inputs = new List<float>() { 9, 14, 2, 0, 10, 0, 0, 0 },
                        actionIndex = 5,
                    },
                    new InputData()
                    {
                        inputs = new List<float>() { 12, 35, 2, 0, 10, 0, 0, 0 },
                        actionIndex = 3,
                    },
                    new InputData()
                    {
                        inputs = new List<float>() { 15, 56, 2, 0, 10, 0, 0, 0 },
                        actionIndex = 1,
                    },
                    new InputData()
                    {
                        inputs = new List<float>() { 18, 92, 2, 0, 10, 0, 0, 0 },
                        actionIndex = 4,
                    },
                    new InputData()
                    {
                        inputs = new List<float>() { 21, 128, 2, 0, 10, 0, 0, 0 },
                        actionIndex = 2,
                    },
                    new InputData()
                    {
                        inputs = new List<float>() { 24, 114, 2, 1, 10, 0, 0, 0 },
                        actionIndex = 1,
                    },
                };

                return res;
            }
            else
            {
                List<InputData> res = new()
                {
                    new InputData()
                    {
                        inputs = new List<float>() { 3, 22, 0, 0, 10, 0, 0, 0 },
                        actionIndex = 0,
                    },
                    new InputData()
                    {
                        inputs = new List<float>() { 6, 28, 1, 0, 10, 0, 0, 0 },
                        actionIndex = 0,
                    },
                    new InputData()
                    {
                        inputs = new List<float>() { 9, 14, 2, 0, 10, 0, 0, 0 },
                        actionIndex = 1,
                    },
                    new InputData()
                    {
                        inputs = new List<float>() { 12, 35, 2, 1, 10, 0, 0, 0 },
                        actionIndex = (int)TaskType.SetDefPitModeForSingle,
                    },
                    new InputData()
                    {
                        inputs = new List<float>() { 15, 56, 2, 0, 10, 0, 0, 0 },
                        actionIndex = 1,
                    },
                    new InputData()
                    {
                        inputs = new List<float>() { 18, 92, 2, 1, 10, 0, 0, 0 },
                        actionIndex = (int)TaskType.SetDefPitModeForSingle,
                    },
                    new InputData()
                    {
                        inputs = new List<float>() { 21, 128, 2, 1, 10, 0, 0, 0 },
                        actionIndex = 5,
                    },
                    new InputData()
                    {
                        inputs = new List<float>() { 24, 114, 2, 1, 10, 0, 0, 0 },
                        actionIndex = 5,
                    },
                };

                return res;
            }
        }
    }

    public static List<InputData> ConvertToInputData(Dictionary<InputKey, int> inputActionSet)
    {
        List<InputData> result = new();

        foreach (var item in inputActionSet)
        {
            var key = item.Key;

            float[] zaebal = key.ToArra();

            InputData inputData = new();
            inputData.inputs = zaebal.ToList();
            inputData.actionIndex = item.Value;

            result.Add(inputData);
        }

        return result;
    }
    
}

[System.Serializable]
public class HolderJoson
{
    public List<InputData> data;
}


[System.Serializable]
public class InputData
{
    public List<float> inputs;
    public int actionIndex; 
    public bool winable;
    public int countWin;
    public int countDefeat;
}