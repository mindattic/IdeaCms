/* IdeaCms.Component.ModalPopup — Activator for .ma-modal overlays.
 * Handles X-button clicks, backdrop clicks, and Escape key via event delegation.
 * Fires CustomEvent('ma-modal-close', {bubbles:true}) on the .ma-modal element before hiding.
 * Blazor-managed modals: close state is owned by Blazor (@onclick); the JS fires the event
 * and hides the element — harmless because Blazor's re-render removes the element entirely
 * once its backing state clears. Safe to load more than once. No external requests. */
(function () {
  'use strict';
  if (window.__maModalPopup) return;
  window.__maModalPopup = true;

  function closeModal(modal) {
    modal.dispatchEvent(new CustomEvent('ma-modal-close', { bubbles: true }));
    modal.hidden = true;
  }

  document.addEventListener('click', function (e) {
    var target = e.target;
    if (!(target instanceof Element)) return;

    // X button (or any element with .ma-modal-close) inside a .ma-modal
    var closeBtn = target.closest('.ma-modal-close');
    if (closeBtn) {
      var modal = closeBtn.closest('.ma-modal');
      if (modal) closeModal(modal);
      return;
    }

    // Backdrop click: the click landed directly on the .ma-modal element itself
    if (target.classList && target.classList.contains('ma-modal') && target.dataset.closeBackdrop !== 'false') {
      closeModal(target);
    }
  });

  document.addEventListener('keydown', function (e) {
    if (e.key !== 'Escape') return;
    var modals = document.querySelectorAll('.ma-modal:not([hidden]):not([data-close-escape="false"])');
    for (var i = modals.length - 1; i >= 0; i--) closeModal(modals[i]);
  });
})();
