Base icon design was done by @caroott

# Create Electron icon set

**It is assumed, that the icon file in question is called `icon.svg`**

1. Create local Node.js project.

```bash
npm init -y
```

2. Replace the contents of `package.json` with the following:

```json
{
    "name": "icons",
    "version": "1.0.0",
    "description": "",
    "license": "ISC",
    "author": "",
    "type": "commonjs",
    "main": "index.js",
    "scripts": {
        "test": "echo \"Error: no test specified\" && exit 1"
    },
    "devDependencies": {
        "electron-icon-maker": "^0.0.5",
        "sharp": "^0.35.5"
    }
}
```

3. Install the required dependencies.

```bash
npm install
```

4. Generate `icon.png` from `icon.svg`.

`node -e "require('sharp')('icon.svg', { density: 300 }).resize(1024, 1024, { fit: 'contain', background: { r: 0, g: 0, b: 0, alpha: 0 } }).png().toFile('icon.png')"`

5. Generate icon set.

`npx electron-icon-maker --input="./icon.png" --output="./out"`

6. Place the content of the `out/icons` folder into this folder.

7. Done 🎉

# Ressources

The idea was taken from https://www.youtube.com/watch?v=XjD97J386EI
