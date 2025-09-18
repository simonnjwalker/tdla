<?php
require_once "c:/php/pear/Mail.php";
require __DIR__ . "/info.php";

// Collect and sanitize POST values
$name    = trim($_POST['name'] ?? '');
$email   = trim($_POST['email'] ?? '');
$number  = trim($_POST['number'] ?? '');
$subject = trim($_POST['subject'] ?? 'Contact Form Submission');
$message = trim($_POST['message'] ?? '');

// Simple validation
if ($name === '' || !filter_var($email, FILTER_VALIDATE_EMAIL) || $message === '') {
    http_response_code(400);
    exit("Invalid form submission.");
}

// Construct email body
$body = "You have received a new contact form submission:\n\n"
      . "Name:    $name\n"
      . "Email:   $email\n"
      . "Phone:   $number\n"
      . "Subject: $subject\n\n"
      . "Message:\n$message\n";

// Headers
$headers = array(
    'From'    => $from,
    'To'      => $to,
    'Subject' => $subject,
    'Reply-To'=> $email
);

// SMTP settings
$smtp = Mail::factory('smtp', array(
    'host'     => $host,
    'auth'     => true,
    'username' => $username,
    'password' => $password
));

// Send mail
$mail = $smtp->send($to, $headers, $body);

// Handle result
if (PEAR::isError($mail)) {
    http_response_code(500);
    echo "Mailer Error: " . $mail->getMessage();
} else {
    echo "Message successfully sent!";
}
