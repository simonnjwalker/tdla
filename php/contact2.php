<?php
// If you used Composer:
//require __DIR__ . '/vendor/autoload.php';

// If you uploaded PHPMailer manually, comment out the line above and use:
require __DIR__ . '/src/PHPMailer.php';
require __DIR__ . '/src/SMTP.php';
require __DIR__ . '/src/Exception.php';

use PHPMailer\PHPMailer\PHPMailer;
use PHPMailer\PHPMailer\Exception;

if ($_SERVER['REQUEST_METHOD'] !== 'POST') {
    http_response_code(405);
    exit('Method Not Allowed');
}

// Basic validation + honeypot
$name    = trim($_POST['name'] ?? '');
$email   = trim($_POST['email'] ?? '');
$message = trim($_POST['message'] ?? '');
$trap    = trim($_POST['website'] ?? ''); // honeypot

if ($trap !== '') {
    http_response_code(400);
    exit('Bad Request');
}

if ($name === '' || $email === '' || $message === '' || !filter_var($email, FILTER_VALIDATE_EMAIL)) {
    http_response_code(422);
    exit('Please provide a valid name, email, and message.');
}

// Create mailer
$mail = new PHPMailer(true);

try {
    // SERVER SETTINGS (Plesk/aspwebhosting)
    $mail->isSMTP();
    $mail->Host       = 'mail.tdla.com.au'; // replace with your SMTP host from Plesk
    $mail->SMTPAuth   = true;
    $mail->Username   = 'admin@tdla.com.au'; // the mailbox you created in Plesk
    $mail->Password   = 'YOUR_STRONG_PASSWORD';   // its password
    $mail->Port       = 587;                      // typically 587 (STARTTLS) or 465 (SMTPS)
    $mail->SMTPSecure = PHPMailer::ENCRYPTION_STARTTLS; // try STARTTLS first
    $mail->CharSet    = 'UTF-8';

    // It’s Windows hosting—ensure OpenSSL is available for TLS:
    if (!extension_loaded('openssl')) {
        throw new Exception('The OpenSSL PHP extension is required for TLS.');
    }

    // FROM/TO
    // Use a domain mailbox in From to avoid spam flags:
    $mail->setFrom('admin@tdla.com.au', 'Website Contact Form');
    // Let replies go to the user who filled the form:
    $mail->addReplyTo($email, $name);
    // Your destination address (could be the same as Username or another inbox/alias):
    $mail->addAddress('admin@tdla.com.au', 'Site Admin');

    // CONTENT
    $mail->isHTML(true);
    $mail->Subject = 'New Contact Form Submission';
    $mail->Body    = '
        <p><strong>Name:</strong> '.htmlspecialchars($name).'</p>
        <p><strong>Email:</strong> '.htmlspecialchars($email).'</p>
        <p><strong>Message:</strong><br>'.nl2br(htmlspecialchars($message)).'</p>
        <hr>
        <p style="font-size:12px;color:#666">Sent from '.$_SERVER['HTTP_HOST'].'</p>
    ';
    $mail->AltBody = "Name: $name\nEmail: $email\n\nMessage:\n$message";

    // SEND
    $mail->send();
    echo 'Message sent. Thanks for reaching out!';
} catch (Exception $e) {
    http_response_code(500);
    echo 'Mailer error: ' . htmlspecialchars($mail->ErrorInfo ?: $e->getMessage());
}