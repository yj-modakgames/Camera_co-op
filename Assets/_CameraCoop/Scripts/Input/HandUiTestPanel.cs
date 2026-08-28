using UnityEngine;
using UnityEngine.UI;

namespace CameraCoop
{
    public sealed class HandUiTestPanel : MonoBehaviour
    {
        [SerializeField] private HandButtonInteractable testA;
        [SerializeField] private HandButtonInteractable testB;
        [SerializeField] private HandButtonInteractable testC;
        [SerializeField] private Text resultLabel;

        private bool subscribed;

        public int TestACount { get; private set; }
        public int TestBCount { get; private set; }
        public int TestCCount { get; private set; }

        private void OnEnable()
        {
            if (subscribed)
            {
                return;
            }
            if (testA == null || testB == null || testC == null || resultLabel == null
                || testA == testB || testA == testC || testB == testC)
            {
                Debug.LogError("HandUiTestPanel requires three distinct hand buttons and a result label.", this);
                enabled = false;
                return;
            }
            testA.OnHandClick += ConfirmA;
            testB.OnHandClick += ConfirmB;
            testC.OnHandClick += ConfirmC;
            subscribed = true;
        }

        private void OnDisable()
        {
            if (!subscribed)
            {
                return;
            }
            if (testA != null)
            {
                testA.OnHandClick -= ConfirmA;
            }
            if (testB != null)
            {
                testB.OnHandClick -= ConfirmB;
            }
            if (testC != null)
            {
                testC.OnHandClick -= ConfirmC;
            }
            subscribed = false;
        }

        private void ConfirmA(HandClickContext context)
        {
            TestACount++;
            ShowConfirmation("A", TestACount);
        }

        private void ConfirmB(HandClickContext context)
        {
            TestBCount++;
            ShowConfirmation("B", TestBCount);
        }

        private void ConfirmC(HandClickContext context)
        {
            TestCCount++;
            ShowConfirmation("C", TestCCount);
        }

        private void ShowConfirmation(string buttonName, int count)
        {
            if (resultLabel != null)
            {
                resultLabel.text = "테스트 " + buttonName + " 확인 · " + count + "회";
            }
        }
    }
}
