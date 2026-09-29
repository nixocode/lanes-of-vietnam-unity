// Smoke test for the scripted Photoshop pipeline (PLAN §12.9: "Adobe work must
// be reproducible"). Creates a document, fills it, saves a PNG, closes it, and
// returns what it did — so a run proves Photoshop answers, draws and writes.
//   tools/photoshop/run.sh tools/photoshop/smoke.jsx /path/out.png
(function () {
    var out = new File(arguments.length ? arguments[0] : "~/Desktop/lov-smoke.png");
    app.displayDialogs = DialogModes.NO;
    var prevUnits = app.preferences.rulerUnits;
    app.preferences.rulerUnits = Units.PIXELS;
    var doc = app.documents.add(64, 64, 72, "lov-smoke", NewDocumentMode.RGB, DocumentFill.WHITE);
    var c = new SolidColor();
    c.rgb.red = 90; c.rgb.green = 104; c.rgb.blue = 60;   // olive drab
    doc.selection.selectAll();
    doc.selection.fill(c);
    var opts = new PNGSaveOptions();
    doc.saveAs(out, opts, true, Extension.LOWERCASE);
    doc.close(SaveOptions.DONOTSAVECHANGES);
    app.preferences.rulerUnits = prevUnits;
    return "ok " + app.version + " " + out.fsName;
})();
