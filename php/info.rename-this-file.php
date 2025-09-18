<?php

// 2025-09-18 1.0.2 SNJW rename this file as info.php and update with real info below
// (the actual settings file is not in source control)
const SMTP_HOST   = 'mail.tdla.com.au';
const SMTP_PORT   = 25;        // 465 for SSL, 587 for TLS
const SMTP_USER   = 'admin@tdla.com.au';
const SMTP_PASS   = 'PASSWORD_GOES_HERE';
const SMTP_SECURE = 'tls';      // 'ssl' | 'tls' | '' (none)

// From / To
const MAIL_FROM      = 'no-reply@tdla.com.au';
const MAIL_FROM_NAME = 'TDLA Website Contact Form';
const MAIL_TO        = 'secretary.tdla@gmail.com';
const MAIL_TO_NAME   = 'TDLA Secretary';

// Optional: set true while testing to see SMTP conversation in output
const SMTP_DEBUG = false;

?>