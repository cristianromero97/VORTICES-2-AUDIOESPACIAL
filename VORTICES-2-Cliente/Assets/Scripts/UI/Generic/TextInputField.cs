using TMPro;
using UnityEngine;

namespace Vortices
{
    public class TextInputField : MonoBehaviour
    {
        [SerializeField] public TMP_InputField inputfield;

        [SerializeField] public TextMeshProUGUI placeholder;
        [SerializeField] public TextMeshProUGUI text;

        private string placeholderText;

        private void Start()
        {
            placeholderText = placeholder.text;
            text.text = "";

            // If there is a virtual keyboard, it will subscribe to it
            GameObject keyboard = GameObject.Find("Keyboard Canvas");
            if (keyboard != null)
            {
                HandKeyboard handKeyboard = keyboard.GetComponent<HandKeyboard>();
                inputfield.onSelect.AddListener(delegate
                {
                    if (handKeyboard != null)
                        handKeyboard.SetInputField(inputfield);
                });
            }
        }

        private void OnDestroy()
        {
            if (inputfield != null)
                inputfield.onSelect.RemoveAllListeners();
        }

        public string GetData()
        {
            return inputfield.text;
        }
        public int GetDataInt()
        {
            try
            {
                return int.Parse(inputfield.text);
            }
            catch
            {
                return 0;
            }
        
        }

        public void SetText(string text)
        {
            inputfield.text = text;
        }

        public void ClearPlaceholderText()
        {
            placeholder.text = "";
        }

        public void RestorePlaceholderText()
        {
            placeholder.text = placeholderText;
        }
    }
}
