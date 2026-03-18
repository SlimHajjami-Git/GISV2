const sharp = require('sharp');
const path = require('path');
const fs = require('fs');

const logo = path.join(__dirname, 'src', 'assets', 'icon', 'calypso-logo.png');
const androidRes = path.join(__dirname, 'android', 'app', 'src', 'main', 'res');

const mipmapSizes = {
  'mipmap-mdpi': 48,
  'mipmap-hdpi': 72,
  'mipmap-xhdpi': 96,
  'mipmap-xxhdpi': 144,
  'mipmap-xxxhdpi': 192,
};

async function generate() {
  for (const [folder, size] of Object.entries(mipmapSizes)) {
    const dir = path.join(androidRes, folder);
    if (!fs.existsSync(dir)) fs.mkdirSync(dir, { recursive: true });

    // ic_launcher.png (square with padding)
    await sharp(logo)
      .resize(size, size, { fit: 'contain', background: { r: 255, g: 255, b: 255, alpha: 1 } })
      .png()
      .toFile(path.join(dir, 'ic_launcher.png'));

    // ic_launcher_round.png (same image, Android handles the round mask)
    await sharp(logo)
      .resize(size, size, { fit: 'contain', background: { r: 255, g: 255, b: 255, alpha: 1 } })
      .png()
      .toFile(path.join(dir, 'ic_launcher_round.png'));

    // ic_launcher_foreground.png (for adaptive icons, with padding)
    const fgSize = Math.round(size * 1.5); // adaptive icon foreground is larger
    await sharp(logo)
      .resize(fgSize, fgSize, { fit: 'contain', background: { r: 0, g: 0, b: 0, alpha: 0 } })
      .png()
      .toFile(path.join(dir, 'ic_launcher_foreground.png'));

    console.log(`Generated ${folder} (${size}px)`);
  }

  // Splash screen images (portrait)
  const splashSizes = {
    'drawable-port-mdpi': [320, 480],
    'drawable-port-hdpi': [480, 800],
    'drawable-port-xhdpi': [720, 1280],
    'drawable-port-xxhdpi': [960, 1600],
    'drawable-port-xxxhdpi': [1280, 1920],
    'drawable-land-mdpi': [480, 320],
    'drawable-land-hdpi': [800, 480],
    'drawable-land-xhdpi': [1280, 720],
    'drawable-land-xxhdpi': [1600, 960],
    'drawable-land-xxxhdpi': [1920, 1280],
  };

  for (const [folder, [w, h]] of Object.entries(splashSizes)) {
    const dir = path.join(androidRes, folder);
    if (!fs.existsSync(dir)) fs.mkdirSync(dir, { recursive: true });

    const logoSize = Math.min(w, h) * 0.4;
    const logoResized = await sharp(logo)
      .resize(Math.round(logoSize), Math.round(logoSize), { fit: 'contain', background: { r: 0, g: 0, b: 0, alpha: 0 } })
      .png()
      .toBuffer();

    await sharp({
      create: { width: w, height: h, channels: 4, background: { r: 255, g: 255, b: 255, alpha: 1 } }
    })
      .composite([{ input: logoResized, gravity: 'centre' }])
      .png()
      .toFile(path.join(dir, 'splash.png'));

    console.log(`Generated ${folder} splash (${w}x${h})`);
  }

  console.log('All icons and splash screens generated!');
}

generate().catch(console.error);
