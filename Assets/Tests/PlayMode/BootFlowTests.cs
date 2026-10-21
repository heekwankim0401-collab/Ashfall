using System.Collections;
using Ashfall.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Ashfall.Tests.PlayMode
{
    // M0 AC: Boot 씬에서 시작하면 MainMenu 씬이 로드된다.
    public class BootFlowTests
    {
        private const float TimeoutSec = 10f;

        [UnityTest]
        public IEnumerator Boot_LoadsMainMenu()
        {
            SceneManager.LoadScene(SceneNames.Boot);

            float deadline = Time.realtimeSinceStartup + TimeoutSec;
            while (SceneManager.GetActiveScene().name != SceneNames.MainMenu && Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.AreEqual(SceneNames.MainMenu, SceneManager.GetActiveScene().name);
        }
    }
}
