# Pin to top 📌

[![Release](https://img.shields.io/badge/Release-v1.0.3-brightgreen.svg)](https://github.com/raisulsohan/PinToTop/releases)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-blue.svg)](https://github.com/raisulsohan/PinToTop)
[![Developer](https://img.shields.io/badge/Developer-Raisul%20Sohan-orange.svg)](https://github.com/raisulsohan)
[![License](https://img.shields.io/badge/License-MIT-purple.svg)](LICENSE)

সক্রিয় যেকোনো উইন্ডোর minimize, maximize ও close বাটনের বাঁ পাশে একই মাপ ও ডিজাইনের একটি native-style **Pin button** যুক্ত করার একটি লাইটওয়েট উইন্ডোজ ইউটিলিটি।

বাটনটি উইন্ডোর আসল title bar-এর থিম/রং অনুসরণ করে এবং pin state বোঝানোর জন্য সূক্ষ্ম glyph ও accent color ব্যবহার করে। সেটিতে ক্লিক করলেই উইন্ডোটি **Always on top** (সব সময় ওপরে) থাকে; আবার ক্লিক করলে আনপিন হয়ে যায়। এমনকি উইন্ডো টেনে এক জায়গা থেকে আরেক জায়গায় নেওয়ার সময় বা ফোকাস বদলে গেলেও বাটনটি নির্বিঘ্নে উইন্ডোর সাথে অবস্থান বজায় রাখে।

---

## 🚀 ডাউনলোড ও ইনস্টল

- **সর্বশেষ রিলিজ ডাউনলোড**: [GitHub Releases - PinToTop](https://github.com/raisulsohan/PinToTop/releases/latest)
- **Installer (`PinToTopSetup.exe`)**: কোনো Administrator access ছাড়াই বর্তমান ইউজারের লোকাল ডিরেক্টরিতে (`%LocalAppData%\Programs\Pin to top`) ইন্সটল হয়। Start Menu ও Desktop শর্টকাট তৈরি করে এবং চাইলে Windows চালু হওয়ার সাথে অটোমেটিক রান করার অপশন রয়েছে।
- **Uninstall**: উইন্ডোজের সাধারণ **Installed apps** (বা Apps & features) সেটিংস থেকে এক ক্লিকেই সম্পূর্ণ আনইন্সটল করা যায়।

---

## 🧰 Portable ব্যবহার

- সেটআপ ছাড়া সরাসরি চালাতে চাইলে `PinToTop.exe` অথবা সোর্স থেকে `Start Pin to top.cmd` চালান।
- টুলটি চালুর পর System Tray (ঘড়ির পাশে নোটিফিকেশন এরিয়া)-তে চলে যায়।
- Tray icon-এ রাইট ক্লিক করে **Run at Windows startup**, **About Pin to top**, অথবা **Exit** বেছে নেওয়া যায়।

---

## ⚙️ প্রয়োজনীয়তা ও বিল্ড

- **অপারেটিং সিস্টেম**: Windows 10 বা Windows 11 (64-bit)
- সাধারণ ব্যবহারে কোনো Administrator অধিকারের প্রয়োজন নেই। (তবে কোনো তৃতীয় পক্ষের সফটওয়্যার যদি Administrator হিসেবে চালানো থাকে, তার topmost স্ট্যাটাস টগল করতে এই টুলটিও Administrator হিসেবে চালানো লাগতে পারে)।
- সোর্স থেকে নতুন করে EXE ও Setup রিবিল্ড করতে চাইলে কেবল `Build-Installer.cmd`-তে ডাবল-ক্লিক করলেই PS2EXE ও Inno Setup ব্যবহার করে স্বয়ংক্রিয়ভাবে নতুন প্যাকেজ তৈরি হয়ে যাবে।

---

## 👨‍💻 Developer & Credits

- **Developer**: **Raisul Sohan**
- **GitHub**: [@raisulsohan](https://github.com/raisulsohan)
- **Repository**: [https://github.com/raisulsohan/PinToTop](https://github.com/raisulsohan/PinToTop)
- **Email**: lettertosohan@gmail.com
- **Copyright**: © 2026 Raisul Sohan. All rights reserved.

---

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
