using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;


[System.Serializable]
public class NeuronState
{
    public float[,] weightsHidden_1;
    public float[,] weightsHidden_2;
    public float[,] weightsOutputLayer;

    public float[] thresoldsHidden_1;
    public float[] thresoldsHidden_2;
    public float[] thresoldsOutputLayer;

    public float aStep;
    public float curWeightRange;
}

public class NeuronNetwork : MonoBehaviour
{
    [SerializeField] float megaErrorThresold = 0.03f;
    [SerializeField] int CountHiddenNeurons_1 = 10;
    [SerializeField] int CountHiddenNeurons_2 = 10;
    [SerializeField] float a = 0.1f;
    [SerializeField] float weightRange = 0.038f;

    [Space]

    [SerializeField] List<InputData> savesos;
    [SerializeField]
    LineRenderer lineRenderer;
    [SerializeField]
    LineRenderer lineOutput;

    public int LearnIteration { get; private set; }
    public int CountNaNError { get; private set; }

    public float aStep;
    public float curWeightRange;
    public float curErr;
    public float minErr;

    int countOutputNeuron;
    int countHiddenNeuron_1;
    int countHiddenNeuron_2;
    int p;

    

    //[SerializeField]
    //List<float> weights = new List<float>();
    [SerializeField]
    float[] etalons;

    [SerializeField]
    float[] outputs;
    //float threshold;

    float[,] weightsHidden_1;
    float[,] weightsHidden_2;
    float[,] weightsOutputLayer;

    float[] weightedSumsHidden_1;
    float[] weightedSumsHidden_2;
    float[] weightedSumsOutput;

    float[] outputsHidden_1;
    float[] outputsHidden_2;


    [SerializeField]
    float[] thresoldsHidden_1;
    [SerializeField]
    float[] thresoldsHidden_2;
    [SerializeField]
    float[] thresoldsOutputLayer;

    bool teaching = true;

    float learningCheckpointTimer;
    

    Player player;
    AIBehaviour aIBehaviour;

    private IEnumerator Start()
    {
        aStep = a;
        curWeightRange = weightRange;

        yield return null;

        player = GetComponent<Player>();
        aIBehaviour = GetComponent<AIBehaviour>();
        countOutputNeuron = aIBehaviour.actionsSet.Count;
        savesos = JsonXyeson.LoadData(player.Team);

        if (savesos == null)
        {
            teaching = false;
        }
        else
        {
            NetInit();
            LoadWeights();
        }

        StartCoroutine(NeuroLearning());
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.L))
        {
            if (teaching)
            {
                teaching = false;
                EventsHolder.neuronTeached?.Invoke();
            }
        }
    }

    IEnumerator NeuroLearning()
    {
        int iter = LearnIteration;
        while (teaching)
        {
            iter++;
            yield return null;

            bool matched = true;
            float[] allErrors = new float[savesos.Count];

            for (int iSample = 0; iSample < savesos.Count; iSample++)
            {
                //int stoshaIndex = Random.Range(0, savesos.Count);
                //var stoshaSample = savesos[stoshaIndex];

                for (int i = 0; i < countHiddenNeuron_1; i++)
                {
                    float wSum = 0;
                    for (int j = 0; j < p; j++)
                    {
                        float x = savesos[iSample].inputs[j];
                        wSum += weightsHidden_1[i, j] * x;
                    }
                    wSum -= thresoldsHidden_1[i];
                    weightedSumsHidden_1[i] = wSum;
                    outputsHidden_1[i] = ReLUActivation(wSum);
                    //outputsHidden_1[i] = Sigmoda(wSum);
                }

                for (int i = 0; i < countHiddenNeuron_2; i++)
                {
                    float wSum = 0;
                    for (int j = 0; j < countHiddenNeuron_1; j++)
                    {
                        float x = outputsHidden_1[j];
                        wSum += weightsHidden_2[i, j] * x;
                    }
                    wSum -= thresoldsHidden_2[i];
                    weightedSumsHidden_2[i] = wSum;
                    //outputsHidden_2[i] = Sigmoda(wSum);
                    outputsHidden_2[i] = ReLUActivation(wSum);
                }

                // Вычисление выходных значений выходного слоя
                for (int i = 0; i < countOutputNeuron; i++)
                {
                    float weightedSumOutputLayer = 0;
                    for (int j = 0; j < countHiddenNeuron_2; j++)
                    {
                        float x = outputsHidden_2[j];
                        weightedSumOutputLayer += weightsOutputLayer[i, j] * x;
                    }
                    weightedSumOutputLayer -= thresoldsOutputLayer[i];
                    weightedSumsOutput[i] = weightedSumOutputLayer;
                    outputs[i] = ReLUActivation(weightedSumOutputLayer);//weightedSumOutputLayer;
                }

                var maxOut = outputs.Max();
                int actionIndex = 0;
                for (int i = 0; i < countOutputNeuron; i++)
                {
                    if (Mathf.Approximately(maxOut, outputs[i]))
                    {
                        actionIndex = i;
                    }
                }

                lineOutput.SetPosition(iSample, new Vector3(iSample, actionIndex));

                //=========================================================
                // Если хотя бы один раз выходы не совпали, значит сеть необучена
                var dif = savesos[iSample].actionIndex - actionIndex;
                if (Mathf.Abs(dif) > 0.1f)
                {
                    matched = false;
                }
                //=========================================================

                //yield return null;
                // Изминение порогов и весовых коэффициентов
                // Ебаный пиздец...
                int actIdx = (int)savesos[iSample].actionIndex;

                for (int i = 0; i < countOutputNeuron; i++)
                {
                    if (i == actIdx)
                    {
                        etalons[i] = 0.98f;
                    }
                    else
                    {
                        etalons[i] = Mathf.Clamp(outputs[i], 0f, 0.7f);
                    }
                }
                //Вычисение ошибок для выходного слоя
                float[] errsOutput = new float[countOutputNeuron];
                for (int i = 0; i < countOutputNeuron; i++)
                {
                    errsOutput[i] = outputs[i] - etalons[i];
                    //if (i == actIdx)
                    //{
                    //    if(etalons[i] < outputs[i])
                    //    {
                    //        errsOutput[i] = 0.0000001f;
                    //    }
                    //}
                    //else
                    //{

                    //    //if (outputs[actIdx] > outputs[i])
                    //    //{
                    //    //    errsOutput[i] = 0;
                    //    //}
                    //    //else
                    //    //{
                    //    //    errsOutput[i] = outputs[i] - etalons[i];
                    //    //}
                    //}
                }
                // Вычисеие для второго скрытного слоя
                float[] errHiden_2 = new float[countHiddenNeuron_2];
                for (int i = 0; i < countHiddenNeuron_2; i++)
                {
                    float errSum = 0;
                    for (int j = 0; j < countOutputNeuron; j++)
                    {
                        //errHiden[i] = errOutput * DerivativeReLU(weightedSumOutputLayer) * weightsOutputLayer[i];
                        //errSum += errsOutput[j] * DerivativeSigmodos(weightedSumsOutput[j]) * weightsOutputLayer[j, i];
                        errSum += errsOutput[j] * DerivativeReLU(weightedSumsOutput[j]) * weightsOutputLayer[j, i];
                    }
                    errHiden_2[i] = errSum;
                }

                // Вычисение для первого скрытно ёбыря
                float[] errHiden_1 = new float[countHiddenNeuron_1];
                for (int i = 0; i < countHiddenNeuron_1; i++)
                {
                    float errSum = 0;
                    for (int j = 0; j < countHiddenNeuron_2; j++)
                    {
                        //errSum += errHiden_2[j] * DerivativeSigmodos(weightedSumsHidden_2[j]) * weightsHidden_2[j, i];
                        errSum += errHiden_2[j] * DerivativeReLU(weightedSumsHidden_2[j]) * weightsHidden_2[j, i];
                    }
                    errHiden_1[i] = errSum;
                }


                //if(iter > 500)
                //{
                //    a = 0.0005f;
                //}



                // Корректировка весов от скрытого слоя к выходному i -> j
                //yield return null;
                for (int i = 0; i < countOutputNeuron; i++)
                {
                    for (int j = 0; j < countHiddenNeuron_2; j++)
                    {
                        float x = outputsHidden_2[j];
                        //float w = weightsOutputLayer[i, j] - a * errsOutput[i] * x * DerivativeSigmodos(weightedSumsOutput[i]);
                        float w = weightsOutputLayer[i, j] - aStep * errsOutput[i] * x * DerivativeReLU(weightedSumsOutput[i]);
                        weightsOutputLayer[i, j] = w;
                    }
                    //thresoldsOutputLayer[i] = thresoldsOutputLayer[i] + a * errsOutput[i] * DerivativeSigmodos(weightedSumsOutput[i]);
                    thresoldsOutputLayer[i] = thresoldsOutputLayer[i] + aStep * errsOutput[i] * DerivativeReLU(weightedSumsOutput[i]);

                }

                // Корректировка весов от входного слоя к скрытому
                for (int i = 0; i < countHiddenNeuron_2; i++)
                {
                    for (int j = 0; j < countHiddenNeuron_1; j++)
                    {
                        float w = weightsHidden_2[i, j];
                        float x = outputsHidden_1[j];
                        //w = w - a * errHiden[i] * DerivativeReLU(weightedSums[i]) * x;
                        //w = w - a * errHiden_2[i] * DerivativeSigmodos(weightedSumsHidden_2[i]) * x;
                        w = w - aStep * errHiden_2[i] * DerivativeReLU(weightedSumsHidden_2[i]) * x;
                        weightsHidden_2[i, j] = w;
                    }
                    //thresoldsHidden[i] = thresoldsHidden[i] + a * errHiden[i] * DerivativeReLU(weightedSums[i]);
                    //thresoldsHidden_2[i] = thresoldsHidden_2[i] + a * errHiden_2[i] * DerivativeSigmodos(weightedSumsHidden_2[i]);
                    thresoldsHidden_2[i] = thresoldsHidden_2[i] + aStep * errHiden_2[i] * DerivativeReLU(weightedSumsHidden_2[i]);

                }

                for (int i = 0; i < countHiddenNeuron_1; i++)
                {
                    for (int j = 0; j < p; j++)
                    {
                        float w = weightsHidden_1[i, j];
                        float x = savesos[iSample].inputs[j];
                        //w = w - a * errHiden[i] * DerivativeReLU(weightedSums[i]) * x;
                        //w = w - a * errHiden_1[i] * DerivativeSigmodos(weightedSumsHidden_1[i]) * x;
                        w = w - aStep * errHiden_1[i] * DerivativeReLU(weightedSumsHidden_1[i]) * x;

                        weightsHidden_1[i, j] = w;
                    }
                    //thresoldsHidden[i] = thresoldsHidden[i] + a * errHiden[i] * DerivativeReLU(weightedSums[i]);
                    //thresoldsHidden_1[i] = thresoldsHidden_1[i] + a * errHiden_1[i] * DerivativeSigmodos(weightedSumsHidden_1[i]);
                    thresoldsHidden_1[i] = thresoldsHidden_1[i] + aStep * errHiden_1[i] * DerivativeReLU(weightedSumsHidden_1[i]);

                }

                yield return null;

                // Суммарная ошибка сети
                float sumErr = 0;
                for (int i = 0; i < countOutputNeuron; i++)
                {
                    sumErr += errsOutput[i] * errsOutput[i];
                }
                allErrors[iSample] = sumErr;

                learningCheckpointTimer += Time.deltaTime;
            }

            float MEGA_ERROR = 0;
            for (int i = 0; i < savesos.Count; i++)
            {
                MEGA_ERROR += allErrors[i];
            }
            MEGA_ERROR *= 0.5f;
            curErr = MEGA_ERROR;
            if(minErr > curErr)
            {
                minErr = curErr;
            }
            LearnIteration = iter;
            if (GameManager.Inst.printNeuroLog)
            {
                print($"{gameObject.name} Ошибкос {MEGA_ERROR} || а вот хуяция: {iter} || Совпадение выходов {matched}");
            }

            if (MEGA_ERROR < megaErrorThresold || matched)
            {
                teaching = false;
                JsonXyeson.SaveNeuronState(player.Team, GetState());
                EventsHolder.neuronTeached?.Invoke();
                learningCheckpointTimer = 0;
                PlayerPrefs.SetInt($"CurIteration{player.Team}", 0);
                PlayerPrefs.Save();
                CountNaNError = 0;
                aStep = a;
                curWeightRange = weightRange;
            }

            if (float.IsNaN(MEGA_ERROR))
            {
                CountNaNError++;
                if (CountNaNError > 1)
                {
                    aStep -= 0.001f;
                    aStep = Mathf.Clamp(aStep, 0.001f, 1f);
                    curWeightRange -= 0.001f;
                    curWeightRange = Mathf.Clamp(curWeightRange, 0.001f, 1f);

                    JsonXyeson.SaveNeuronState(player.Team, GetState());
                }
                NetInit();
                print("++++++++++++++++++++++++++++++");
            }

            if (learningCheckpointTimer > 88)
            {
                JsonXyeson.SaveNeuronState(player.Team, GetState());
                learningCheckpointTimer = 0;
                print("****** Сохранил стейт ******");
                PlayerPrefs.SetInt($"CurIteration{player.Team}", LearnIteration);
                PlayerPrefs.Save();
            }
        }
    }

    NeuronState GetState()
    {
        NeuronState state = new()
        {
            weightsHidden_1 = weightsHidden_1,
            weightsHidden_2 = weightsHidden_2,
            weightsOutputLayer = weightsOutputLayer,

            thresoldsHidden_1 = thresoldsHidden_1,
            thresoldsHidden_2 = thresoldsHidden_2,
            thresoldsOutputLayer = thresoldsOutputLayer,

            aStep = aStep,
            curWeightRange = curWeightRange,
        };

        return state;
    }

    void NetInit()
    {
        countHiddenNeuron_1 = CountHiddenNeurons_1;//[0].inputishe.Count;// / 2;
        countHiddenNeuron_2 = CountHiddenNeurons_2;
        p = savesos[0].inputs.Count;

        weightsHidden_1 = new float[countHiddenNeuron_1, p];
        weightsHidden_2 = new float[countHiddenNeuron_2, countHiddenNeuron_1];
        thresoldsHidden_1 = new float[countHiddenNeuron_1];
        thresoldsHidden_2 = new float[countHiddenNeuron_2];
        thresoldsOutputLayer = new float[countOutputNeuron];
        outputsHidden_1 = new float[countHiddenNeuron_1];
        outputsHidden_2 = new float[countHiddenNeuron_2];
        weightsOutputLayer = new float[countOutputNeuron, countHiddenNeuron_2];
        weightedSumsHidden_1 = new float[countHiddenNeuron_1];
        weightedSumsHidden_2 = new float[countHiddenNeuron_2];
        weightedSumsOutput = new float[countOutputNeuron];

        etalons = new float[countOutputNeuron];
        outputs = new float[countOutputNeuron];

        lineRenderer.positionCount = savesos.Count;
        lineOutput.positionCount = savesos.Count;
        for (int i = 0; i < savesos.Count; i++)
        {
            lineRenderer.SetPosition(i, new Vector3(i, savesos[i].actionIndex));
        }

        lineRenderer.transform.position -= lineRenderer.transform.right * (savesos.Count / 2);
        lineOutput.transform.position -= lineOutput.transform.right * (savesos.Count / 2);

        //=============================
        for (int i = 0; i < countOutputNeuron; i++)
        {
            for (int j = 0; j < countHiddenNeuron_2; j++)
            {
                weightsOutputLayer[i, j] = Random.Range(-curWeightRange, curWeightRange);
            }
            thresoldsOutputLayer[i] = Random.Range(-curWeightRange, curWeightRange);
        }


        for (int i = 0; i < countHiddenNeuron_1; i++)
        {
            for (int j = 0; j < p; j++)
            {
                weightsHidden_1[i, j] = Random.Range(-curWeightRange, curWeightRange);
            }

            thresoldsHidden_1[i] = Random.Range(-curWeightRange, curWeightRange);

        }

        for (int i = 0; i < countHiddenNeuron_2; i++)
        {
            for (int j = 0; j < countHiddenNeuron_1; j++)
            {
                weightsHidden_2[i, j] = Random.Range(-curWeightRange, curWeightRange);
            }
            thresoldsHidden_2[i] = Random.Range(-curWeightRange, curWeightRange);
        }

        minErr = float.MaxValue;
    }

    void LoadWeights()
    {
        var state = JsonXyeson.LoadNeuronState(player.Team);
        if (state != null)
        {
            weightsHidden_1 = state.weightsHidden_1;
            weightsHidden_2 = state.weightsHidden_2;
            weightsOutputLayer = state.weightsOutputLayer;

            thresoldsHidden_1 = state.thresoldsHidden_1;
            thresoldsHidden_2 = state.thresoldsHidden_2;
            thresoldsOutputLayer = state.thresoldsOutputLayer;
            print("Загрузил веса");

            if (PlayerPrefs.HasKey($"CurIteration{player.Team}"))
            {
                LearnIteration = PlayerPrefs.GetInt($"CurIteration{player.Team}");
            }

            if (state.aStep > 0)
            {
                aStep = state.aStep;
            }
            if (state.curWeightRange > 0)
            {
                curWeightRange = state.curWeightRange;
            }
        }
    }

    float ReLUActivation(float weightedSum)
    {
        if (weightedSum > 0)
            return weightedSum;
        else
            return 0.01f * weightedSum;
    }

    float DerivativeReLU(float weightedSum)
    {
        if (weightedSum > 0)
            return 1;
        else
            return 0.01f;
    }

    public int GetActionIdx(InputKey inputKey)
    {
        var zaebal = inputKey.ToArra();

        for (int i = 0; i < countHiddenNeuron_1; i++)
        {
            float wSum = 0;
            for (int j = 0; j < p; j++)
            {
                float x = zaebal[j];
                wSum += weightsHidden_1[i, j] * x;
            }
            wSum -= thresoldsHidden_1[i];
            weightedSumsHidden_1[i] = wSum;
            outputsHidden_1[i] = ReLUActivation(wSum);
            //outputsHidden_1[i] = Sigmoda(wSum);
        }

        for (int i = 0; i < countHiddenNeuron_2; i++)
        {
            float wSum = 0;
            for (int j = 0; j < countHiddenNeuron_1; j++)
            {
                float x = outputsHidden_1[j];
                wSum += weightsHidden_2[i, j] * x;
            }
            wSum -= thresoldsHidden_2[i];
            weightedSumsHidden_2[i] = wSum;
            //outputsHidden_2[i] = Sigmoda(wSum);
            outputsHidden_2[i] = ReLUActivation(wSum);
        }

        // Вычисление выходных значений выходного слоя
        for (int i = 0; i < countOutputNeuron; i++)
        {
            float weightedSumOutputLayer = 0;
            for (int j = 0; j < countHiddenNeuron_2; j++)
            {
                float x = outputsHidden_2[j];
                weightedSumOutputLayer += weightsOutputLayer[i, j] * x;
            }
            weightedSumOutputLayer -= thresoldsOutputLayer[i];
            weightedSumsOutput[i] = weightedSumOutputLayer;
            outputs[i] = ReLUActivation(weightedSumOutputLayer);//weightedSumOutputLayer;
        }

        var maxOut = outputs.Max();
        int actionIndex = 100;
        for (int i = 0; i < countOutputNeuron; i++)
        {
            if (Mathf.Abs(maxOut - outputs[i]) < 0.01f)
            {
                actionIndex = i;
            }
        }
        print($"{gameObject.name} ### {(TaskType)actionIndex} ### {inputKey.playtime}");
        return actionIndex;
    }
}

[System.Serializable]
public struct InputKey
{
    public int playtime;
    public int gold;
    public int countWorkers;
    public int countSimple;
    public int countTurrels;
    public int iron;
    public int countBarracks;
    public int countMachineFactoryes;

    public float[] ToArra()
    {
        float[] zaebal = new float[8];

        zaebal[(int)InputType.Playtime]      = playtime;
        zaebal[(int)InputType.Gold]          = gold;
        zaebal[(int)InputType.CountSimple]   = countSimple;
        zaebal[(int)InputType.CountWorkers]  = countWorkers;
        zaebal[(int)InputType.Iron]          = iron;
        zaebal[(int)InputType.CountTurrels]  = countTurrels;
        zaebal[(int)InputType.CountBarracks] = countBarracks;
        zaebal[(int)InputType.CountMachineFactoryes] = countMachineFactoryes;

        return zaebal;
    }
}

public enum InputType : int
{
    Playtime              = 0,
    Gold                  = 1,
    CountWorkers          = 2,
    CountSimple           = 3,
    Iron                  = 4,
    CountTurrels          = 5,
    CountBarracks         = 6,
    CountMachineFactoryes = 7,
}
