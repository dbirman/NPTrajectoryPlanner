mergeInto(LibraryManager.library, {

  Copy2Clipboard: function (str) {
	str = UTF8ToString(str);
    console.log('copy2clipboard called: ' + str);
	copy2clipboardStr = str;
	canvas.addEventListener('click', copy2clipboardCallback, false);
  },

  SelectPID: function (pid) {
    window.alert(UTF8ToString(pid));
  },

  DownloadFile: function(filename, filedata) {
    downloadFile(filename, filedata);
  }

});