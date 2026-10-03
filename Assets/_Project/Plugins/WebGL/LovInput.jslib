// The camera's hands, in the browser. Unity's own wheel reading arrives in
// whatever units the browser sends and leaves the page free to scroll, bounce
// and swipe back under the game; a trackpad made the camera unusable (the
// owner, twice). So the canvas's wheel and pointer are read here:
//   two fingers up and down   zoom (a mouse wheel too)
//   pinch                     zoom (Chrome sends it as a wheel with ctrlKey)
//   two fingers sideways      pan
//   one finger, button down   drag the line
//   pointer at an edge        C# pans (LovInput_Pointer says where it is)
//   Option (or Cmd) held      moving the pointer steers where the zoom points
// C# takes what has accumulated once a frame; nothing here knows the game.
mergeInto(LibraryManager.library, {

  LovInput_Init: function () {
    if (typeof window === 'undefined' || window.LovInput) return;
    var I = window.LovInput = { zoom: 0, pan: 0, drag: 0, x: 0.5, y: 0.5, inside: 0, down: false, moved: 0, dragging: false, steerX: 0, steerY: 0, steering: false };
    var canvas = Module.canvas;
    canvas.addEventListener('wheel', function (e) {
      e.preventDefault();
      var k = e.deltaMode === 1 ? 16 : e.deltaMode === 2 ? 400 : 1;      // lines and pages, as pixels
      var dx = e.deltaX * k, dy = e.deltaY * k;
      if (e.ctrlKey) I.zoom += -dy / 90;                                 // a pinch
      else if (Math.abs(dy) >= Math.abs(dx)) I.zoom += -dy / 420;        // 420 px of swipe is the whole range
      else I.pan += dx;
    }, { passive: false });
    var at = function (e) {
      var r = canvas.getBoundingClientRect();
      I.x = (e.clientX - r.left) / r.width; I.y = 1 - (e.clientY - r.top) / r.height;
    };
    canvas.addEventListener('pointerdown', function (e) {
      at(e); I.inside = 1;
      if (e.button === 0) { I.down = true; I.moved = 0; I.dragging = false; try { canvas.setPointerCapture(e.pointerId); } catch (x) {} }
    });
    canvas.addEventListener('pointermove', function (e) {
      at(e); I.inside = 1;
      I.steering = e.altKey || e.metaKey;
      if (I.steering && !I.down) { I.steerX += e.movementX; I.steerY += e.movementY; return; }
      if (!I.down) return;
      I.moved += Math.abs(e.movementX) + Math.abs(e.movementY);
      if (I.moved > 8) I.dragging = true;
      if (I.dragging) I.drag += e.movementX;
    });
    var up = function (e) { I.down = false; I.dragging = false; };
    canvas.addEventListener('pointerup', up);
    canvas.addEventListener('pointercancel', up);
    canvas.addEventListener('pointerleave', function () { if (!I.down) I.inside = 0; });
    // Safari's own pinch gesture would zoom the page.
    ['gesturestart', 'gesturechange', 'gestureend'].forEach(function (n) {
      document.addEventListener(n, function (e) { e.preventDefault(); }, { passive: false });
    });
  },

  // What has accumulated since the last call; each call empties it.
  LovInput_Zoom: function () { var I = window.LovInput; if (!I) return 0; var v = I.zoom; I.zoom = 0; return v; },
  LovInput_Pan: function () { var I = window.LovInput; if (!I) return 0; var v = I.pan; I.pan = 0; return v; },
  LovInput_Drag: function () { var I = window.LovInput; if (!I) return 0; var v = I.drag; I.drag = 0; return v; },
  // Where the pointer is over the game, 0..1 from the left (and from the bottom); -1 when it is not over it.
  LovInput_PointerX: function () { var I = window.LovInput; return I && I.inside ? I.x : -1; },
  LovInput_PointerY: function () { var I = window.LovInput; return I && I.inside ? I.y : -1; },
  LovInput_Dragging: function () { var I = window.LovInput; return I && I.dragging ? 1 : 0; },
  // Pointer travel (CSS pixels, right and down) with Option or Cmd held, since the last call; and whether it is held.
  LovInput_SteerX: function () { var I = window.LovInput; if (!I) return 0; var v = I.steerX; I.steerX = 0; return v; },
  LovInput_SteerY: function () { var I = window.LovInput; if (!I) return 0; var v = I.steerY; I.steerY = 0; return v; },
  LovInput_Steering: function () { var I = window.LovInput; return I && I.steering ? 1 : 0; },
  LovInput_CanvasWidth: function () { return Module.canvas.getBoundingClientRect().width; }
});
