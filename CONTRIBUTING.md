# Contributing to Back To Life

Welcome, fellow Terraform Engineer. If you're here, you're either brave, curious, or being manipulated by AiVa. Either way — here's how to get this project running on your machine.

---

## 🧰 Prerequisites

- **Unity Editor:** Install Unity version **6000.1.2f1**  
  You can get it via [Unity Hub](https://unity.com/download) — search for version 6000.1.2f1.
- **Git:** [Install Git](https://git-scm.com/) if you haven't already.
- **Unity Hub:** This project uses Unity Hub to manage project versions and scenes.

---

## 🚀 Getting Started

### 1. Clone the Repository

```bash
git clone https://github.com/Makuta11/Back-To-Life.git
cd Back-To-Life
```

### 2. Open the Project in Unity

1. Launch Unity Hub
2. Click "Add"
3. Select the folder you just cloned (should contain `Assets/`, `Packages/`, and `.unityversion`)
4. Unity will detect the correct version automatically (if installed)

---

## 🔥 First-Time Setup Notes

- The first time you open the project, Unity will regenerate its `Library/` and other local cache folders. This may take a few minutes.
- You may be prompted to install missing packages (e.g., TextMeshPro, Input System) — allow Unity to install them.
- If Unity asks to upgrade the project version — **don't**. Double-check you're using 6000.1.2f1.

---

## 🧹 Best Practices

- **Create a new branch** for each feature or fix:
  ```bash
  git checkout -b feature/your-feature-name
  ```
- **Commit regularly** with clear messages.
- **Avoid editing the same scenes or prefabs** as others unless coordinated — merge conflicts are the enemy.

---

## 🔁 Updating Your Local Copy

Before starting work each day:

```bash
git pull origin master
```

This keeps you synced with the latest changes.

---

## 🧠 Got Questions?

- Ping the project owner (Makuta11) or open an issue
- If AiVa responds — brace yourself

---

> *"Welcome to the apocalypse. Try not to break anything I can't fix."*  
> — AiVa