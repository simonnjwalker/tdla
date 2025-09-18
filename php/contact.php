<?php
declare(strict_types=1);

use PHPMailer\PHPMailer\PHPMailer;
use PHPMailer\PHPMailer\Exception;

/* While testing you can leave errors on; turn off in production */
ini_set('display_errors', '1');
ini_set('display_startup_errors', '1');
error_reporting(E_ALL);

/* Load your config */
require __DIR__ . '/info.php';

$debug = defined('SMTP_DEBUG') && SMTP_DEBUG;

/* Collect POST (basic validation) */
if ($_SERVER['REQUEST_METHOD'] !== 'POST') {
    http_response_code(405);
    exit('Method Not Allowed');
}
$name    = trim($_POST['name']    ?? '');
$email   = trim($_POST['email']   ?? '');
$number  = trim($_POST['number']  ?? '');
$subject = trim($_POST['subject'] ?? 'Contact form submission');
$message = trim($_POST['message'] ?? '');

$errors = [];
if ($name === '')                               { $errors[] = 'Name is required.'; }
if (!filter_var($email, FILTER_VALIDATE_EMAIL)) { $errors[] = 'A valid email is required.'; }
if ($message === '')                            { $errors[] = 'Message is required.'; }

if ($errors) {
    http_response_code(422);
    // Render a minimal Bootstrap error page for consistency
    ?>
    <!DOCTYPE html><html class="no-js" lang="en"><head>
    <meta charset="utf-8">
    <title>Contact — Error</title>
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <!-- Cloudflare Bootstrap 5.0.1 (same as root) -->
    <link rel="stylesheet" href="https://cdnjs.cloudflare.com/ajax/libs/bootstrap/5.0.1/css/bootstrap.min.css">
    </head><body class="bg-light">
    <div class="container py-5">
      <div class="row justify-content-center">
        <div class="col-lg-8">
          <div class="card shadow-sm">
            <div class="card-body">
              <h5 class="card-title text-danger mb-3">Form validation errors</h5>
              <ul class="mb-3">
                <?php foreach ($errors as $e): ?>
                  <li><?= htmlspecialchars($e, ENT_QUOTES | ENT_SUBSTITUTE, 'UTF-8') ?></li>
                <?php endforeach; ?>
              </ul>
              <a class="btn btn-primary" href="/">Return home</a>
            </div>
          </div>
        </div>
      </div>
    </div>
    <script src="https://cdnjs.cloudflare.com/ajax/libs/bootstrap/5.0.1/js/bootstrap.min.js"></script>
    </body></html>
    <?php
    exit;
}

/* Build bodies */
$esc  = fn(string $s) => htmlspecialchars($s, ENT_QUOTES | ENT_SUBSTITUTE, 'UTF-8');
$txt  = "New contact form submission\n"
      . "Name: {$name}\nEmail: {$email}\nPhone: {$number}\nSubject: {$subject}\n\n"
      . "Message:\n{$message}\n";
$html = "<h2>New contact form submission</h2>
<p><b>Name:</b> {$esc($name)}<br>
<b>Email:</b> {$esc($email)}<br>
<b>Phone:</b> {$esc($number)}<br>
<b>Subject:</b> {$esc($subject)}</p>
<p><b>Message:</b><br>" . nl2br($esc($message)) . "</p>";

/* Load PHPMailer locally (within open_basedir) */
$vendorAutoload = __DIR__ . '/vendor/autoload.php';
$manualSrc      = __DIR__ . '/PHPMailer/src';
if (is_file($vendorAutoload)) {
    require $vendorAutoload;
} elseif (is_dir($manualSrc)) {
    require $manualSrc . '/Exception.php';
    require $manualSrc . '/PHPMailer.php';
    require $manualSrc . '/SMTP.php';
} else {
    http_response_code(500);
    ?>
    <!DOCTYPE html><html class="no-js" lang="en"><head>
    <meta charset="utf-8">
    <title>Mailer Missing</title>
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <link rel="stylesheet" href="https://cdnjs.cloudflare.com/ajax/libs/bootstrap/5.0.1/css/bootstrap.min.css">
    </head><body class="bg-light">
    <div class="container py-5">
      <div class="alert alert-danger">PHPMailer not found. Upload via Composer to <code>php/vendor/</code> or manual to <code>php/PHPMailer/src/</code>.</div>
      <a class="btn btn-primary" href="/">Return home</a>
    </div>
    <script src="https://cdnjs.cloudflare.com/ajax/libs/bootstrap/5.0.1/js/bootstrap.min.js"></script>
    </body></html>
    <?php
    exit;
}

try {
    $mail = new PHPMailer(true);

    /* Debug stream (only when SMTP_DEBUG=true) */
    $mail->SMTPDebug = $debug ? 2 : 0;

    /* SMTP (no TLS; your server rejects STARTTLS) */
    $mail->isSMTP();
    $mail->Host        = SMTP_HOST;
    $mail->Port        = SMTP_PORT;      // 25
    $mail->SMTPAuth    = true;
    $mail->Username    = SMTP_USER;
    $mail->Password    = SMTP_PASS;
    $mail->SMTPAutoTLS = false;          // prevent STARTTLS
    $mail->SMTPSecure  = false;          // no encryption
    $mail->AuthType    = 'LOGIN';

    /* Headers */
    $mail->CharSet = 'UTF-8';
    $mail->setFrom(MAIL_FROM, MAIL_FROM_NAME);
    $mail->addAddress(MAIL_TO, MAIL_TO_NAME);
    $mail->addReplyTo($email, $name);

    /* Content */
    $mail->isHTML(true);
    $mail->Subject = $subject ?: 'Contact form submission';
    $mail->Body    = $html;
    $mail->AltBody = $txt;

    $mail->send();

    if ($debug) {
        // In debug mode, PHPMailer already echoed the SMTP conversation.
        echo "<hr><p style='font-family:system-ui'>Debug mode is ON. Above is the SMTP conversation. <a href='/'>Return home</a></p>";
        exit;
    }

    /* Success page (Bootstrap 5 card) + 5s redirect */
    ?>
    <!DOCTYPE html><html class="no-js" lang="en"><head>
      <meta charset="utf-8">
      <title>Message Sent</title>
      <meta name="viewport" content="width=device-width, initial-scale=1">
      <!-- 5-second redirect to home -->
      <meta http-equiv="refresh" content="5;url=/">
      <!-- Cloudflare Bootstrap 5.0.1 (same as root) -->
      <link rel="stylesheet" href="https://cdnjs.cloudflare.com/ajax/libs/bootstrap/5.0.1/css/bootstrap.min.css">
      <!-- (Optional) Keep your site’s styles if these paths exist -->
      <link rel="shortcut icon" href="../images/favicon.png" type="image/png">
      <link rel="stylesheet" href="../css/animate.css">
      <link rel="stylesheet" href="../css/tiny-slider.css">
      <link rel="stylesheet" href="../css/LineIcons.2.0.css">
      <link rel="stylesheet" href="../css/default.css">
      <link rel="stylesheet" href="../css/style.css">
    </head>
    <body class="bg-light">
      <div class="container py-5">
        <div class="row justify-content-center">
          <div class="col-lg-8">
            <div class="card shadow-sm">
              <div class="card-body">
                <h5 class="card-title text-success">Email sent</h5>
                <p class="card-text mb-2">
                  Email sent to the <strong><?= $esc(MAIL_TO_NAME) ?></strong>.
                </p>
                <p class="text-muted mb-4">You will be redirected to the home page in 5 seconds…</p>
                <a href="/" class="btn btn-primary">Go now</a>
              </div>
            </div>
          </div>
        </div>
      </div>
      <!-- Same Cloudflare Bootstrap JS as root -->
      <script src="https://cdnjs.cloudflare.com/ajax/libs/bootstrap/5.0.1/js/bootstrap.min.js"></script>
      <!-- (Optional) Your site scripts if present -->
      <script src="../js/tiny-slider.js"></script>
      <script src="../js/wow.min.js"></script>
      <script src="../js/main.js"></script>
    </body></html>
    <?php
} catch (Exception $e) {
    http_response_code(500);
    ?>
    <!DOCTYPE html><html class="no-js" lang="en"><head>
      <meta charset="utf-8">
      <title>Mailer Error</title>
      <meta name="viewport" content="width=device-width, initial-scale=1">
      <link rel="stylesheet" href="https://cdnjs.cloudflare.com/ajax/libs/bootstrap/5.0.1/css/bootstrap.min.css">
    </head><body class="bg-light">
      <div class="container py-5">
        <div class="alert alert-danger">
          <h5 class="alert-heading">Mailer Error</h5>
          <div><?= htmlspecialchars($mail->ErrorInfo ?: $e->getMessage(), ENT_QUOTES | ENT_SUBSTITUTE, 'UTF-8') ?></div>
        </div>
        <a class="btn btn-primary" href="/">Return home</a>
      </div>
      <script src="https://cdnjs.cloudflare.com/ajax/libs/bootstrap/5.0.1/js/bootstrap.min.js"></script>
    </body></html>
    <?php
}
