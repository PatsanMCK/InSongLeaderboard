using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using VRUIControls;

namespace InSongLeaderboard
{
    internal sealed class PanelDragHandle : MonoBehaviour,
        IPointerEnterHandler,
        IPointerExitHandler,
        IPointerDownHandler,
        IPointerUpHandler,
        IInitializePotentialDragHandler,
        IBeginDragHandler,
        IDragHandler,
        IEndDragHandler
    {
        private static readonly Color IdleColor = new Color(0.35f, 0.80f, 1f, 1f);
        private static readonly Color HoverColor = new Color(1f, 0.85f, 0.25f, 1f);

        private Transform _panelTransform;
        private TextMeshProUGUI _label;
        private CanvasGroup _canvasGroup;
        private VRController _grabbingController;
        private Action _released;
        private Vector3 _grabPosition;
        private Quaternion _grabRotation;
        private bool _interactable;
        private bool _hovering;

        internal void Initialize(Transform panelTransform, Action released)
        {
            _panelTransform = panelTransform;
            _released = released;
            _label = GetComponent<TextMeshProUGUI>();
            _canvasGroup = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
            SetInteractable(false);
        }

        internal void SetInteractable(bool value)
        {
            _interactable = value;
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = value ? 1f : 0f;
                _canvasGroup.blocksRaycasts = value;
                _canvasGroup.interactable = value;
            }

            if (_label != null)
            {
                _label.raycastTarget = value;
                _label.color = IdleColor;
            }

            if (!value && _grabbingController != null)
                ReleasePanel(true);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _hovering = true;
            UpdateColor();
            Plugin.log.Debug("Panel drag handle pointer entered.");
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _hovering = false;
            UpdateColor();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!_interactable || _panelTransform == null)
                return;

            var inputModule = EventSystem.current == null
                ? null
                : EventSystem.current.currentInputModule as VRInputModule;
            var pointer = inputModule == null ? null : inputModule.vrPointer;
            var controller = pointer == null ? null : pointer.lastSelectedVrController;
            if (controller == null)
            {
                Plugin.log.Warn("Panel drag press received, but the active VR controller was not available.");
                return;
            }

            _grabbingController = controller;
            _grabPosition = controller.transform.InverseTransformPoint(_panelTransform.position);
            _grabRotation = Quaternion.Inverse(controller.transform.rotation) * _panelTransform.rotation;
            Plugin.log.Info("Panel drag started through the UI handle.");
            UpdateColor();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (_grabbingController == null)
                return;

            ReleasePanel(true);
        }

        public void OnInitializePotentialDrag(PointerEventData eventData)
        {
            eventData.useDragThreshold = false;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (_grabbingController == null)
                OnPointerDown(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            // Movement follows the physical controller in Update(). Implementing
            // IDragHandler keeps this object captured by Unity's input module.
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (_grabbingController != null)
                ReleasePanel(true);
        }

        private void Update()
        {
            if (_grabbingController == null || _panelTransform == null)
                return;

            _grabPosition -= Vector3.forward *
                             (_grabbingController.thumbstick.y * Time.unscaledDeltaTime);
            var targetPosition = _grabbingController.transform.TransformPoint(_grabPosition);
            var targetRotation = _grabbingController.transform.rotation * _grabRotation;
            _panelTransform.SetPositionAndRotation(
                Vector3.Lerp(_panelTransform.position, targetPosition, 12f * Time.unscaledDeltaTime),
                Quaternion.Slerp(_panelTransform.rotation, targetRotation, 7f * Time.unscaledDeltaTime));
        }

        private void OnDisable()
        {
            if (_grabbingController != null)
                ReleasePanel(true);
        }

        private void ReleasePanel(bool notify)
        {
            _grabbingController = null;
            UpdateColor();
            if (!notify)
                return;

            Plugin.log.Info("Panel drag released through the UI handle.");
            if (_released != null)
                _released();
        }

        private void UpdateColor()
        {
            if (_label != null)
                _label.color = _interactable && (_hovering || _grabbingController != null)
                    ? HoverColor
                    : IdleColor;
        }
    }
}
