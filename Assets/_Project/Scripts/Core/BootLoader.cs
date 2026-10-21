using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ashfall.Core
{
    // M0: Boot 씬에 하나만 둔다. 시작하면 첫 화면 씬(기본 MainMenu)을 불러온다.
    public sealed class BootLoader : MonoBehaviour
    {
        [SerializeField] private string firstScene = SceneNames.MainMenu;

        private void Start()
        {
            SceneManager.LoadSceneAsync(firstScene, LoadSceneMode.Single);
        }
    }
}
