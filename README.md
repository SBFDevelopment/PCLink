# Package-Template

A clean starter template for creating custom Unity packages compatible with the VRChat Creator Companion (VCC).

Packages created with this template are automatically served via the **SBF Global Package Repository**.

[🌐 View Repository Manifest](https://development.chirpter.com/vcc.json) • [⚡ Add Directly to VCC](https://www.google.com/search?q=vcc://vpm/addRepo%3Furl%3Dhttps://development.chirpter.com/vcc.json)

---

## 🚀 Getting Started

### 1. Create Your Repository

Click the button below to generate a new repository from this template:

[![Create From Template](https://user-images.githubusercontent.com/737888/185467681-e5fdb099-d99f-454b-8d9e-0760e5a6e588.png)](https://github.com/SBFDevelopment/package-template/generate)

### 2. Local Setup

Clone your newly created repository directly into your Unity project's `Packages` directory:

```bash
cd YourUnityProject/Packages
git clone https://github.com/SBFDevelopment/your-new-repo-name.git

```

### 3. Configure Package Metadata

Open and update the `package.json` file in the root of your repository with your package details.

> ⚠️ **Important:** Leave the `"version"` field exactly as it is. Version numbers are automatically handled and populated during the release pipeline.

---

## 📦 Automated Release Pipeline

Publishing updates and adding your package to the VCC listing is fully automated:

1. When you are ready to publish, create a Pull Request to the **`release`** branch (if the branch doesn't exist yet, go ahead and create it).
2. Merging or pushing to the `release` branch triggers an automated GitHub Action workflow that:
* Increments the package version number.
* Generates a new GitHub Release with the bundled `.zip` asset.
* Updates the global [vcc-manifest](https://github.com/SBFDevelopment/vcc-manifest) index so VCC users receive the update instantly.
