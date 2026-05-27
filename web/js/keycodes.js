// HID usage codes (Usage Page 0x07). Ported from ch57x-keyboard-tool WellKnownCode.
// Declaration order starting at A=0x04, incrementing by 1.  code = 0x04 + index.
const KEYCODE_ORDER = [
  ["A","A"],["B","B"],["C","C"],["D","D"],["E","E"],["F","F"],["G","G"],["H","H"],
  ["I","I"],["J","J"],["K","K"],["L","L"],["M","M"],["N","N"],["O","O"],["P","P"],
  ["Q","Q"],["R","R"],["S","S"],["T","T"],["U","U"],["V","V"],["W","W"],["X","X"],
  ["Y","Y"],["Z","Z"],
  ["1","1"],["2","2"],["3","3"],["4","4"],["5","5"],["6","6"],["7","7"],["8","8"],["9","9"],["0","0"],
  ["Enter","Enter"],["Escape","Esc"],["Backspace","Backspace"],["Tab","Tab"],["Space","Space"],
  ["Minus","- _"],["Equal","= +"],["LeftBracket","[ {"],["RightBracket","] }"],["Backslash","\\ |"],
  ["NonUSHash","# ~"],["Semicolon","; :"],["Quote","' \""],["Grave","` ~"],["Comma",", <"],
  ["Dot",". >"],["Slash","/ ?"],["CapsLock","CapsLock"],
  ["F1","F1"],["F2","F2"],["F3","F3"],["F4","F4"],["F5","F5"],["F6","F6"],
  ["F7","F7"],["F8","F8"],["F9","F9"],["F10","F10"],["F11","F11"],["F12","F12"],
  ["PrintScreen","PrtSc"],["ScrollLock","ScrLk"],["Pause","Pause"],["Insert","Insert"],
  ["Home","Home"],["PageUp","PgUp"],["Delete","Delete"],["End","End"],["PageDown","PgDn"],
  ["Right","→"],["Left","←"],["Down","↓"],["Up","↑"],["NumLock","NumLock"],
  ["NumPadSlash","NP /"],["NumPadAsterisk","NP *"],["NumPadMinus","NP -"],["NumPadPlus","NP +"],
  ["NumPadEnter","NP Enter"],["NumPad1","NP 1"],["NumPad2","NP 2"],["NumPad3","NP 3"],
  ["NumPad4","NP 4"],["NumPad5","NP 5"],["NumPad6","NP 6"],["NumPad7","NP 7"],["NumPad8","NP 8"],
  ["NumPad9","NP 9"],["NumPad0","NP 0"],["NumPadDot","NP ."],["NonUSBackslash","NonUS \\"],
  ["Application","Menu"],["Power","Power"],["NumPadEqual","NP ="],
  ["F13","F13"],["F14","F14"],["F15","F15"],["F16","F16"],["F17","F17"],["F18","F18"],
  ["F19","F19"],["F20","F20"],["F21","F21"],["F22","F22"],["F23","F23"],["F24","F24"],
];

// name -> { code, label }
const KEYCODES = {};
KEYCODE_ORDER.forEach(([name, label], i) => {
  KEYCODES[name] = { code: 0x04 + i, label };
});

// HID modifier bits (standard).
const MODIFIERS = {
  Ctrl:       0x01,
  Shift:      0x02,
  Alt:        0x04,
  Win:        0x08,
  RightCtrl:  0x10,
  RightShift: 0x20,
  RightAlt:   0x40,
  RightWin:   0x80,
};

// 16-bit consumer (media) usages.
const MEDIA_CODES = {
  Next:        { code: 0xb5, label: "다음 곡" },
  Previous:    { code: 0xb6, label: "이전 곡" },
  Stop:        { code: 0xb7, label: "정지" },
  Play:        { code: 0xcd, label: "재생/일시정지" },
  Mute:        { code: 0xe2, label: "음소거" },
  VolumeUp:    { code: 0xe9, label: "볼륨 +" },
  VolumeDown:  { code: 0xea, label: "볼륨 -" },
  Favorites:   { code: 0x182, label: "즐겨찾기" },
  Calculator:  { code: 0x192, label: "계산기" },
  ScreenLock:  { code: 0x19e, label: "화면 잠금" },
};

const MOUSE_BUTTONS = { Left: 0x01, Right: 0x02, Middle: 0x04 };
const MOUSE_MODIFIERS = { Ctrl: 0x01, Shift: 0x02, Alt: 0x04 };

// ASCII char -> {mods, code} for 상용구(text macro), US layout.
const CHAR_TO_ACCORD = {};
for (let c = 97; c <= 122; c++) {                 // a-z / A-Z
  const lo = String.fromCharCode(c), up = lo.toUpperCase();
  CHAR_TO_ACCORD[lo] = { mods: [], code: up };
  CHAR_TO_ACCORD[up] = { mods: ["Shift"], code: up };
}
const _digits = "1234567890", _shiftDigits = "!@#$%^&*()";
for (let i = 0; i < 10; i++) {
  CHAR_TO_ACCORD[_digits[i]] = { mods: [], code: _digits[i] };
  CHAR_TO_ACCORD[_shiftDigits[i]] = { mods: ["Shift"], code: _digits[i] };
}
const _sym = {
  " ": ["Space", 0], "\n": ["Enter", 0], "\t": ["Tab", 0],
  "-": ["Minus", 0], "_": ["Minus", 1], "=": ["Equal", 0], "+": ["Equal", 1],
  "[": ["LeftBracket", 0], "{": ["LeftBracket", 1], "]": ["RightBracket", 0], "}": ["RightBracket", 1],
  "\\": ["Backslash", 0], "|": ["Backslash", 1], ";": ["Semicolon", 0], ":": ["Semicolon", 1],
  "'": ["Quote", 0], "\"": ["Quote", 1], "`": ["Grave", 0], "~": ["Grave", 1],
  ",": ["Comma", 0], "<": ["Comma", 1], ".": ["Dot", 0], ">": ["Dot", 1],
  "/": ["Slash", 0], "?": ["Slash", 1],
};
for (const [ch, [code, sh]] of Object.entries(_sym)) CHAR_TO_ACCORD[ch] = { mods: sh ? ["Shift"] : [], code };

export { KEYCODES, KEYCODE_ORDER, MODIFIERS, MEDIA_CODES, MOUSE_BUTTONS, MOUSE_MODIFIERS, CHAR_TO_ACCORD };
