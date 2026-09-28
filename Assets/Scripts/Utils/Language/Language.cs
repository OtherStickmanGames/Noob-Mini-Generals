using UnityEngine;
using TMPro;

public class Language : MonoBehaviour
{
	public static bool Rus => Application.systemLanguage == SystemLanguage.Russian;

	public static void SetLanguage(TextMeshProUGUI text)
	{
		if (Application.systemLanguage == SystemLanguage.English)
			return;
		if (Application.systemLanguage != SystemLanguage.Russian)
			return;

		switch (text.text)
		{
			case "New levels are in development. We are working on a project, soon we will add new weapons, abilities, maps, enemies and bosses.":
				{
					text.text = "Новые уровни находятся в разработке. Мы работаем над проектом, скоро добавим новое оружие, способности, карты, врагов и боссов.";
					break;
				}
			case "Blood Color":
				{
					text.text = "Цвет Крови";
					break;
				}
			case "Elixir":
				{
					text.text = "Эликсир";
					break;
				}
			case "Staff":
				{
					text.text = "Палка";
					break;
				}
			case "Cudgel":
				{
					text.text = "Дубина";
					break;
				}
			case "Shadow Attack":
				{
					text.text = "Теневой Удар";
					break;
				}
			case "Shadow Thorns":
				{
					text.text = "Теневыe Шипы";
					break;
				}
			case "Blast Wave":
				{
					text.text = "Взрывная Волна";
					break;
				}
			case "Candy":
				{
					text.text = "Конфета";
					break;
				}
			case "Shovel":
				{
					text.text = "Лопата";
					break;
				}
			case "Sword":
				{
					text.text = "Меч";
					break;
				}
			case "Wind Shuriken":
				{
					text.text = "Ветрянной сюрекен";
					break;
				}
			case "Stave":
				{
					text.text = "Посох";
					break;
				}
			case "Battle Cudgel":
				{
					text.text = "Боевая Дубина";
					break;
				}
			case "Scythe":
				{
					text.text = "Коса";
					break;
				}
			case "More Games":
				{
					text.text = "Больше Игр";
					break;
				}
			case "SHOP":
				{
					text.text = "МАГАЗИН";
					break;
				}
			case "Level is Completed!":
                {
					text.text = "Уровень завершен";
					break;
                }
			case "Coins Earned:":
				{
					text.text = "Монет Заработано:";
					break;
				}
			case "Restart":
                {
					text.text = "Заново";
					break;
                }
			case "GAME OVER":
                {
					text.text = "КОНЕЦ ИГРЫ";
					break;
                }
			case "Endless Level is Over":
                {
					text.text = "Бесконечный уровень окончен";
					break;
				}
			case "Earned:":
				{
					text.text = "Заработано:";
					break;
				}
			case "Loading...":
				{
					text.text = "Загрузка...";
					break;
				}
			case "= 1x Wave on the Endless Map":
                {
					text.text = "= 1х за одну пройденную волну бесконечного уровня";
					break;
				}
			case "= Regeneration 30%":
				{
					text.text = "= Регенерация 30%";
					break;
				}
			case "Level Up Evolution of Weapon!":
				{
					text.text = "Новый уровень эволюции  Оружия!";
					break;
				}
			case "Now you're fighting differently":
				{
					text.text = "Теперь у тебя другой стиль боя";
					break;
				}
			case "Level Up!":
				{
					text.text = "Новый уровень!";
					break;
				}
			case "Health Point":
				{
					text.text = "Очки жизни";
					break;
				}
			case "Damage Factor":
				{
					text.text = "Усиление Урона";
					break;
				}
			case "Armor":
				{
					text.text = "Броня";
					break;
				}
			case "Health Regen":
				{
					text.text = "Регенерация";
					break;
				}
			case "Resume":
				{
					text.text = "Продолжить";
					break;
				}
			case "Sounds Volume":
				{
					text.text = "Громкость Звуков";
					break;
				}
			case "Menu":
				{
					text.text = "Меню";
					break;
				}
			case "Leave":
				{
					text.text = "Выйти";
					break;
				}
			case "+exp":
				{
					text.text = "+опыт";
					break;
				}
			case "You've got a new level!\nWhen you complete any map, a menu will open to improve the characteristics":
                {
					text.text = "Вы получили новый уровень!\nКогда вы завершите любую карту, то откроется меню для улучшения характеристик";
					break;
                }
		}
	}
}
