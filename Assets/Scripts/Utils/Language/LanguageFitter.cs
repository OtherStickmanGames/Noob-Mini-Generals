using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class LanguageFitter : MonoBehaviour
{
	void Start()
	{
		Language.SetLanguage(GetComponent<TextMeshProUGUI>());
	}

}
