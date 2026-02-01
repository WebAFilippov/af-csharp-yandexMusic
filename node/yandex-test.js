import { spawn } from 'child_process';
import { fileURLToPath } from 'url';
import { dirname, join } from 'path';
import readline from 'readline';

const __filename = fileURLToPath(import.meta.url);
const __dirname = dirname(__filename);

const CSHARP_EXE = join(__dirname, '..', 'MediaControllerService', 'bin', 'Debug', 'net8.0-windows10.0.17763.0', 'win-x64', 'MediaControllerService.exe');

let currentTrack = null;
let isPlaying = false;
let volumeStep = 3; // Default 3%

console.log('🎵 Яндекс Музыка - Media Controller\n');
console.log('C# Service:', CSHARP_EXE);
console.log('');

// Spawn C# process
const service = spawn(CSHARP_EXE, [], {
  windowsHide: false,
  stdio: ['pipe', 'pipe', 'pipe']
});

// Handle stderr (debug output)
service.stderr.on('data', (data) => {
  const lines = data.toString().trim().split('\n');
  lines.forEach(line => {
    if (line.trim()) {
      console.log(`[C#] ${line}`);
    }
  });
});

// Handle stdout (JSON messages)
const rl = readline.createInterface({
  input: service.stdout,
  crlfDelay: Infinity
});

rl.on('line', (line) => {
  try {
    const msg = JSON.parse(line);
    handleMessage(msg);
  } catch (e) {
    if (line.trim()) {
      console.log(`[C#] ${line}`);
    }
  }
});

function handleMessage(msg) {
  if (msg.type === 'session') {
    updateDisplay(msg.data);
  }
}

function updateDisplay(session) {
  // console.clear();
  console.log('🎵 Яндекс Музыка Controller\n');
  
  if (!session) {
    console.log('⚠️  Яндекс Музыка не запущена или нет активного трека\n');
    currentTrack = null;
    isPlaying = false;
  } else {
    currentTrack = session;
    isPlaying = session.playbackStatus === 'Playing';
    
    const statusIcon = isPlaying ? '▶️' : '⏸️';
    console.log(`${statusIcon} Сейчас играет:\n`);
    console.log(`   🎵 ${session.title}`);
    if (session.artist) {
      console.log(`   👤 ${session.artist}`);
    }
    if (session.album) {
      console.log(`   💿 ${session.album}`);
    }
    if (session.thumbnailBase64) {
      const sizeKB = Math.round(session.thumbnailBase64.length * 0.75 / 1024);
      console.log(`   🖼️  Обложка: ${sizeKB}KB`);
      console.log(`   🖼️  Обложка: ${session.thumbnailBase64}`);
    }
    // Add volume info
    if (session.volume !== undefined) {
      const muteIcon = session.isMuted ? '🔇' : '🔊';
      console.log(`   ${muteIcon} Громкость: ${session.volume}%`);
    }
    console.log();
  }

  console.log('Управление:');
  console.log('  p:     Играть/Пауза');
  console.log('  n:     Следующий трек');
  console.log('  b:     Предыдущий трек');
  console.log('  +:     Громкость +');
  console.log('  -:     Громкость -');
  console.log('  m:     Mute/Unmute');
  console.log('  s:     Шаг громкости (3% / 5% / 10%)');
  console.log('  q:     Выход');
  console.log();
  console.log('> ');
}

function sendCommand(command, stepPercent = null) {
  const msg = { command };
  if (stepPercent !== null) {
    msg.stepPercent = stepPercent;
  }
  service.stdin.write(JSON.stringify(msg) + '\n');
  console.log(`📤 Отправлено: ${command}${stepPercent ? ' (step: ' + stepPercent + '%)' : ''}\n`);
}

// Handle keyboard input
const stdin = process.stdin;

if (stdin.isTTY) {
  stdin.setRawMode(true);
  stdin.resume();
  stdin.setEncoding('utf8');

  stdin.on('data', (key) => {
    // Ctrl+C
    if (key === '\u0003') {
      console.log('\n👋 Выход...');
      service.stdin.write(JSON.stringify({ command: 'close' }) + '\n');
      service.kill();
      process.exit(0);
    }

    handleKey(key);
  });
} else {
  // Non-TTY mode - use readline
  console.log('Non-interactive mode. Type commands:');
  console.log('  Commands: p (play/pause), n (next), b (prev), q (quit)');
  console.log('');
  
  const rlInput = readline.createInterface({
    input: process.stdin,
    output: process.stdout
  });
  
  rlInput.setPrompt('> ');
  rlInput.prompt();
  
  rlInput.on('line', (input) => {
    handleKey(input.trim());
    rlInput.prompt();
  });
}

function handleKey(key) {
  switch (key.toLowerCase()) {
    case 'q':
    case 'quit':
    case 'exit':
      console.log('\n👋 Выход...');
      service.stdin.write(JSON.stringify({ command: 'close' }) + '\n');
      setTimeout(() => {
        service.kill();
        process.exit(0);
      }, 500);
      break;
    case 'p':
    case 'play':
    case 'pause':
    case 'playpause':
      sendCommand('playpause');
      break;
    case 'n':
    case 'next':
      sendCommand('next');
      break;
    case 'b':
    case 'prev':
    case 'previous':
      sendCommand('previous');
      break;
    case '+':
    case '=':
      sendCommand('volume_up', volumeStep);
      break;
    case '-':
    case '_':
      sendCommand('volume_down', volumeStep);
      break;
    case 'm':
    case 'mute':
      sendCommand('toggle_mute');
      break;
    case 's':
    case 'step':
      // Cycle through volume steps: 3 -> 5 -> 10 -> 3
      if (volumeStep === 3) volumeStep = 5;
      else if (volumeStep === 5) volumeStep = 10;
      else volumeStep = 3;
      console.log(`\n🔧 Шаг громкости изменен: ${volumeStep}%\n`);
      break;
    default:
      // Unknown command
      break;
  }
}

// Handle C# process exit
service.on('exit', (code) => {
  console.log(`\n⚠️  C# сервис завершился с кодом ${code}`);
  process.exit(code || 0);
});

service.on('error', (err) => {
  console.log(`\n❌ Ошибка запуска C# сервиса: ${err.message}`);
  process.exit(1);
});

console.log('⌛ Ожидание Яндекс Музыки...\n');
