using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using System;

public class CheckGameVersion : MonoBehaviour
{
    [SerializeField] string companyName;
    [SerializeField] string gameName;

    public Action versionNotMatch;

    private IEnumerator Start()
    {
        var request = UnityWebRequest.Get($"https://play.google.com/store/apps/details?id=com.{companyName}.{gameName}");

        var op = request.SendWebRequest();

        yield return op;

        var searchStr = "null,[[[\"1";
        var index = request.downloadHandler.text.IndexOf(searchStr);

        if (index > 0)
        {
            var text = request.downloadHandler.text.Substring(index + searchStr.Length);
            var version = text.Substring(0, text.IndexOf("\""));
            version = version.Insert(0, "1");

            if (Application.version != version)
            {
                versionNotMatch?.Invoke();
            }
        }
        else
        {
            print("не нашел версию игры");
        }

    }
}
