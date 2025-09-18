<?php
require_once "c:\php\pear\Mail.php";
$from = "admin@tdla.com.au";
$to = "tdla4810@protonmail.com";
$subject = "Hi!";
$body = "Hi,\n\nHow are you?";
$host = "mail.tdla.com.au";
$username = "smtp_username";
$password = "smtp_password";
$headers = array ('From' => $from,
    'To' => $to,
    'Subject' => $subject);
$smtp = Mail::factory('smtp',
    array ('host' => $host,
        'auth' => true,
        'username' => $username,
        'password' => $password
    )
);


$mail = $smtp->send($to, $headers, $body);
 

if (PEAR::isError($mail)) {

echo("
" . $mail->getMessage() . "
");

} else {

echo("

Message successfully sent!

");

}

?>